# Design de Arquitetura — Backend REDE

Este documento especifica **como** construir o backend cujos requisitos de
domínio já estão em
[`2026-08-26-rede-backend-requisitos.md`](./2026-08-26-rede-backend-requisitos.md).
Aquele documento é a fonte da verdade para entidades, endpoints e regras de
negócio; este aqui cobre stack, estrutura de projeto, padrões de código,
estratégia de concorrência, testes, ambiente de desenvolvimento e o
roteiro de fases de implementação.

Decisões já validadas com o usuário: PostgreSQL, arquitetura em camadas
simples (sem CQRS/MediatR), JWT com access token único (sem refresh),
testes unitários + integração, recuperação de senha com envio de e-mail
real, hospedagem a decidir depois (nada amarrado a um provedor), e
entrega em fases sequenciais — cada fase com seu próprio ciclo de
plano/execução, mantendo a mesma arquitetura em todas.

---

## 1. Stack

| Camada | Escolha | Motivo |
|---|---|---|
| Runtime | .NET 10 | Já instalado (`10.0.301`); LTS/atual no momento. |
| API | ASP.NET Core Minimal APIs | Idiomático no .NET atual, menos boilerplate que Controllers; endpoints agrupados por feature com `MapGroup`. |
| Banco | PostgreSQL | Gratuito, roda via Docker, bom suporte a `FOR UPDATE`/transações necessário para as regras de concorrência (seção 5). |
| ORM | EF Core 10 (Npgsql) | Padrão de mercado no ecossistema .NET; migrations versionadas em código. |
| Validação | FluentValidation | Regras condicionais (ex: endereço obrigatório só se `formaEntrega == entrega`) ficam mais legíveis que com DataAnnotations. |
| Hash de senha | `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (isolado) | Implementação testada em produção, sem precisar do framework Identity completo (tabelas/roles que não usaremos). |
| Autenticação | JWT Bearer, access token único (4-8h) | Simplicidade decidida para o MVP — sem refresh token. |
| Docs de API | OpenAPI nativo (.NET) + Scalar UI | Leve, sem dependência extra de Swashbuckle. |
| E-mail | Interface `IEmailSender`, provedor concreto decidido na Fase 2 (Resend/SendGrid/SMTP) | Não trava a arquitetura a um provedor específico agora. |
| Testes | xUnit + Testcontainers (Postgres real) | Cobre também comportamento real do banco (constraints, transações), crítico nas regras de estoque/vagas. |

---

## 2. Estrutura da solução

```
RedeStore-BackEnd/
  RedeStore.sln
  docker-compose.yml              # só o Postgres, para dev local
  src/
    RedeStore.Api/
      Endpoints/                  # um arquivo por feature: ProdutosEndpoints.cs, EventosEndpoints.cs...
      Middleware/                 # ExceptionHandler, etc.
      Program.cs
      appsettings.json
      appsettings.Development.json
    RedeStore.Application/
      Produtos/                   # DTOs, Service, Validators por feature
      Eventos/
      Inscricoes/
      Pedidos/
      Auth/
      Common/                     # interfaces (IEmailSender, ICurrentUser), exceptions de aplicação
    RedeStore.Domain/
      Entities/                   # Usuario, Produto, Variacao, Evento, Inscricao, Pedido, ItemPedido
      Enums/
      Exceptions/                 # DomainException e subtipos
    RedeStore.Infrastructure/
      Persistence/
        RedeStoreDbContext.cs
        Migrations/
        Configurations/           # IEntityTypeConfiguration<T> por entidade
      Repositories/
      Email/                      # implementação concreta de IEmailSender
      Auth/                       # geração/validação de JWT
  tests/
    RedeStore.UnitTests/
    RedeStore.IntegrationTests/
```

Regra de dependência: `Api` → `Application` → `Domain`; `Infrastructure` →
`Application` + `Domain`. `Domain` não depende de nada. Isso é o
suficiente para testar `Application` sem banco (mockando as interfaces de
repositório) sem precisar de mais cerimônia que isso.

### Convenções

- Endpoints organizados por `MapGroup("/produtos")` etc., um arquivo de
  extensão por feature (`app.MapProdutosEndpoints()`), chamado do
  `Program.cs`.
- Serviços de aplicação (`ProdutoService`, `PedidoService`, ...) recebem
  interfaces de repositório via DI, contêm a lógica de negócio, e são o
  que os endpoints chamam — endpoints ficam finos (bind request → chama
  service → mapeia resultado pra `Results.Ok/NotFound/...`).
- DTOs de request/response ficam em `Application/<Feature>/Dtos`, nomeados
  em português para bater 1:1 com os campos do frontend (`nome`, `preco`,
  `dataHora`), conforme já estabelecido no doc de requisitos.

---

## 3. Modelo de dados

Mapeamento direto das entidades do doc de requisitos (seção 1) para
tabelas Postgres via EF Core, com os seguintes pontos de atenção:

- `Usuario.senhaHash` nunca é incluído em nenhum DTO de resposta —
  reforçado com um DTO `UsuarioDto` separado da entidade (nunca serializar
  a entidade EF diretamente).
- `Produto.variacoes` é uma tabela filha (`Variacoes`) com FK para
  `Produto`, não um JSON — precisa ser uma tabela relacional de verdade
  para o `UPDATE ... WHERE estoque >= :qtd` da seção 5 funcionar por
  linha.
- `Produto.tamanhos`/`cores`: persistidos como colunas derivadas
  (recalculadas a partir de `variacoes` no momento do save), conforme
  decisão já registrada no doc de requisitos — evita a API ter que
  recalcular em toda leitura.
- `ItemPedido` é uma tabela filha de `Pedido`, com os campos de snapshot
  (`nome`, `precoUnitario`, `fotoUrl`) copiados no momento da criação —
  **sem** FK obrigatória para `Produto` permanecer válido (o produto pode
  ser deletado depois, o snapshot continua íntegro).
- `Evento.vagasRestantes` **não** é uma coluna persistida — é calculado
  (`vagasTotais - count(inscricoes confirmadas)`) e incluído nos DTOs de
  resposta de `GET /eventos` e `GET /eventos/:id`, como já indicado no doc
  de requisitos.
- `PasswordResetTokens` (nova, não estava no doc de requisitos original):
  `{ id, usuarioId, tokenHash, expiraEm, usadoEm? }` — token em si nunca
  persistido em texto puro, só o hash (mesmo princípio de senha).

Migrations do EF Core versionadas em `Infrastructure/Persistence/Migrations`,
aplicadas via `dotnet ef database update` em dev.

---

## 4. Autenticação e segurança

- Claims do JWT: `sub` (id do usuário), `email`, `papel`. Validação de
  autorização por papel via `RequireAuthorization("Admin")` /
  `RequireAuthorization()` nos grupos de endpoint, mapeado 1:1 com a
  tabela da seção 8 do doc de requisitos.
- Middleware global de exceções (`IExceptionHandler` nativo do ASP.NET
  Core) traduz `DomainException`/subtipos em respostas `ProblemDetails`
  com os códigos já usados no doc de requisitos (`CREDENCIAIS_INVALIDAS`,
  `EMAIL_EM_USO`, etc.) — um `Dictionary<Type, (int status, string codigo)>`
  central evita `try/catch` espalhado pelos endpoints.
- CORS: policy nomeada liberando a origem do Angular, configurável por
  ambiente (`Cors:AllowedOrigin` em appsettings — `http://localhost:4200`
  em dev).
- Segredos (JWT signing key, credenciais de e-mail, connection string):
  `dotnet user-secrets` em dev, variáveis de ambiente em produção. Nunca
  em `appsettings.json` commitado.

---

## 5. Concorrência: estoque e vagas

Os dois pontos que o doc de requisitos identifica como reais riscos de
corrida em produção (não expostos pelo mock em memória):

**Estoque de variação (checkout):**
Dentro de uma transação (`BeginTransactionAsync`), para cada item do
pedido:
```sql
UPDATE "Variacoes"
SET "Estoque" = "Estoque" - @qtd
WHERE "Id" = @id AND "Estoque" >= @qtd
```
Se `RowsAffected == 0` para qualquer item, toda a transação é revertida e
o pedido não é criado (nenhum item parcial) — erro retornado indica qual
item ficou sem estoque suficiente.

**Vagas de evento (inscrição):**
Dentro de uma transação, lock pessimista na linha do evento primeiro:
```sql
SELECT 1 FROM "Eventos" WHERE "Id" = @eventoId FOR UPDATE
```
Isso serializa inscrições concorrentes no mesmo evento. Só depois do lock
é feita a contagem de inscrições `confirmada` e a decisão entre `criada`
/ `ja_inscrito` / `esgotado`, tudo antes do commit.

Ambos os padrões são implementados como métodos dedicados no repositório
(`IVariacaoRepository.DecrementarEstoqueAsync`,
`IEventoRepository.LockAndCountInscricoesConfirmadasAsync`) para manter o
SQL raw isolado da camada de Application — os services de negócio não
sabem que é lock pessimista por baixo, só chamam o método e tratam o
resultado.

---

## 6. Recuperação de senha (fluxo completo)

Estende o doc de requisitos (que só previa `POST /auth/recuperar-senha`
sempre `204`) com o endpoint que faltava para o fluxo funcionar de
verdade:

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| POST | `/auth/recuperar-senha` | pública | `{ email }` → sempre `204` (comportamento já especificado, evita enumeração de conta). Se o e-mail existir, gera token, salva hash + expiração (1h), dispara e-mail via `IEmailSender`. |
| POST | `/auth/redefinir-senha` | pública | `{ token, novaSenha }` → valida token (existe, não expirado, não usado), atualiza `senhaHash`, marca token como usado. Erro `TOKEN_INVALIDO` se falhar qualquer checagem. |

O link enviado por e-mail aponta para uma rota do **frontend** Angular
(ex: `/redefinir-senha?token=...`) que ainda não existe hoje — vai
precisar ser criada em conjunto no frontend quando esta fase for
implementada (fora do escopo deste backend, mas é uma dependência a
avisar quando chegarmos na Fase 2).

---

## 7. Testes

- **Unitários** (`RedeStore.UnitTests`, xUnit): regras de
  `Application` isoladas com repositórios mockados — máquina de estados
  do pedido (`proximoStatus`), cálculo de `vagasRestantes`, validators do
  FluentValidation.
- **Integração** (`RedeStore.IntegrationTests`): `WebApplicationFactory`
  + Testcontainers subindo um Postgres real por execução de suite. Cobre
  end-to-end os fluxos críticos:
  - Criar pedido com estoque insuficiente → rejeitado, nenhum item
    parcialmente decrementado.
  - Duas inscrições "simultâneas" (disparadas em paralelo no teste) na
    última vaga de um evento → só uma vira `confirmada`.
  - Login/cadastro/perfil (happy path + erros `CREDENCIAIS_INVALIDAS`/`EMAIL_EM_USO`).
- Testes rodam via `dotnet test` na raiz da solution; Testcontainers exige
  Docker rodando (já disponível no ambiente).

---

## 8. Ambiente de desenvolvimento

Pendências de setup local (a serem resolvidas na Fase 1 — Fundação):

1. Instalar a ferramenta de migrations: `dotnet tool install --global dotnet-ef`.
2. `docker-compose.yml` na raiz do backend, subindo só o Postgres:
   ```yaml
   services:
     db:
       image: postgres:17
       environment:
         POSTGRES_DB: redestore
         POSTGRES_PASSWORD: postgres
       ports:
         - "5432:5432"
       volumes:
         - redestore-db:/var/lib/postgresql/data
   volumes:
     redestore-db:
   ```
   Subir com `docker compose up -d db`; a API roda direto via `dotnet run`
   (mais rápido para iterar do que containerizar a API em dev).
3. Connection string e JWT signing key configurados via
   `dotnet user-secrets set` no projeto `RedeStore.Api` — nunca em
   `appsettings.json`.
4. CORS liberado para `http://localhost:4200` (porta padrão do
   `ng serve` do frontend Angular) em `appsettings.Development.json`.

Nada além disso precisa ser instalado — .NET SDK, Docker, Git e um editor
(VS Code ou Visual Studio) já estão presentes na máquina.

---

## 9. Roteiro de fases

Cada fase é um ciclo `writing-plans` → execução → revisão independente,
todas seguindo a arquitetura definida acima (sem re-arquitetar entre
fases). Ordem definida por dependência (Pedidos depende de Produtos
existir; Fechamento depende de tudo existir).

### Fase 1 — Fundação
Skeleton da solution (4 projetos + testes), `RedeStoreDbContext` vazio,
Docker Compose, autenticação JWT (middleware, geração/validação de
token, `PasswordHasher`), tratamento de erro global (`IExceptionHandler`
+ `ProblemDetails`), OpenAPI/Scalar, CORS. **Sem features de negócio
ainda.** Critério de pronto: `dotnet run` sobe a API vazia, `/health`
responde, Postgres conecta, `dotnet test` roda (mesmo que só com um teste
trivial).

### Fase 2 — Auth & Usuários
Entidade `Usuario`, todos os endpoints da seção 2 do doc de requisitos
(`login`, `cadastro`, `me`, `perfil`) + o fluxo completo de recuperação
de senha da seção 6 deste doc (inclui escolher e configurar o provedor
de e-mail real). Critério de pronto: fluxo de cadastro → login → acessar
rota autenticada → esqueci a senha → redefinir com token funciona
ponta-a-ponta, com testes de integração cobrindo os erros de negócio.

### Fase 3 — Produtos
Entidades `Produto`/`Variacao`, endpoints da seção 3 do doc de
requisitos (público + admin), validação de "ao menos 1 variação",
recálculo de `tamanhos`/`cores`. Sem decremento de estoque ainda (isso é
Fase 5, junto de Pedidos).

### Fase 4 — Eventos & Inscrições
Entidades `Evento`/`Inscricao`, endpoints das seções 4 e 5 do doc de
requisitos, incluindo o lock pessimista de vagas da seção 5 deste doc e
os 3 resultados de inscrição (`criada`/`ja_inscrito`/`esgotado`).

### Fase 5 — Pedidos
Entidades `Pedido`/`ItemPedido`, endpoint de checkout com o decremento
atômico de estoque da seção 5 deste doc, montagem de snapshot a partir do
produto atual (nunca confiando em preço/nome vindos do client), máquina
de estados (`pago → em_preparo → retirado/entregue`). Depende de Produtos
(Fase 3) já existir.

### Fase 6 — Fechamento
Testes de integração cross-feature restantes, revisão de segurança geral
(revisão de autorização por rota contra a tabela da seção 8 do doc de
requisitos), e documentação de deploy (Dockerfile de produção da API,
guia de variáveis de ambiente) — hospedagem específica ainda não decidida
pelo usuário, então esta fase entrega o artefato de deploy (imagem
Docker) sem amarrar a um provedor.

---

## 10. Fora de escopo (herdado do doc de requisitos, seção 7)

Confirmado que continuam fora do escopo deste planejamento, sem mudança:
carrinho persistido no backend (continua só `localStorage` no frontend),
upload de imagem (continua só URL digitada), gateway de pagamento
(pedido nasce `pago`), paginação nas listagens. Qualquer um desses pode
virar uma fase nova no futuro, mas não faz parte do roteiro acima.
