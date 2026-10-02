# RedeStore-BackEnd

API da RedeStore: autenticação de usuários, loja (produtos com variações de tamanho/cor e pedidos
com controle de estoque) e eventos (com inscrições e controle de vagas).

- **Stack:** .NET 10 (ASP.NET Core Minimal APIs), EF Core, PostgreSQL 17, JWT, FluentValidation, Resend (e-mail)
- **Documentação completa dos endpoints:** [`docs/API.md`](docs/API.md)
- **Especificação OpenAPI 3.1:** [`docs/openapi.json`](docs/openapi.json)
- **Deploy e variáveis de ambiente:** [`docs/deploy.md`](docs/deploy.md)

## Como rodar localmente

Pré-requisitos: .NET SDK 10 e Docker.

```bash
# 1. Sobe o PostgreSQL
docker compose up -d db

# 2. Configura os segredos (uma vez)
cd src/RedeStore.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=redestore;Username=postgres;Password=postgres"
dotnet user-secrets set "Jwt:SigningKey" "troque-por-uma-chave-de-pelo-menos-32-caracteres"
dotnet user-secrets set "Jwt:Issuer" "RedeStore.Local"
dotnet user-secrets set "Jwt:Audience" "RedeStore.Local.Clients"
dotnet user-secrets set "Resend:ApiKey" "sua-chave-resend"
dotnet user-secrets set "Resend:FromEmail" "nao-responda@exemplo.com"
dotnet user-secrets set "Frontend:ResetPasswordUrl" "http://localhost:4200/redefinir-senha"

# 3. Roda a API (as migrations são aplicadas automaticamente na inicialização)
dotnet run
```

A API sobe em `http://localhost:5052`. Em Development ficam disponíveis:

- `http://localhost:5052/scalar` — interface interativa para testar os endpoints
- `http://localhost:5052/openapi/v1.json` — documento OpenAPI ao vivo

### Dados de exemplo (seed de desenvolvimento)

O banco sobe vazio e não existe endpoint para criar admin. Para ter catálogo, eventos e um admin
de teste:

1. Com a API rodando, crie a conta `admin@rede.com` via `POST /auth/cadastro` (pela tela de cadastro
   do front ou pelo Scalar). Crie também uma conta comum, ex.: `jovem@rede.com`.
2. Rode o seed:

   ```powershell
   ./scripts/seed-dev.ps1
   ```

   Ele promove `admin@rede.com` a `admin` e cadastra 7 produtos (com variações e estoque) e 6
   eventos com datas relativas a hoje (um no passado, para testar `apenasFuturos`). É idempotente:
   produtos/eventos só são inseridos se as tabelas estiverem vazias.
3. Faça login de novo com `admin@rede.com` — o papel fica gravado no token.

O SQL fica em [`scripts/seed-dev.sql`](scripts/seed-dev.sql). **Somente para desenvolvimento.**

### Testes

```bash
dotnet test tests/RedeStore.UnitTests          # unitários
dotnet test tests/RedeStore.IntegrationTests   # integração (precisa do Docker rodando — usa Testcontainers)
```

## Estrutura

```
src/
  RedeStore.Api             → endpoints, filtro de validação, tratamento global de erros, OpenAPI
  RedeStore.Application     → serviços (regras de negócio), DTOs, validadores
  RedeStore.Domain          → entidades, enums, exceções de domínio
  RedeStore.Infrastructure  → EF Core/PostgreSQL, repositórios, JWT, hash de senha, e-mail
tests/
  RedeStore.UnitTests
  RedeStore.IntegrationTests
docs/                       → documentação (API, OpenAPI, deploy, design)
scripts/gerar-openapi.ps1   → regenera docs/openapi.json
scripts/seed-dev.ps1        → popula o banco local com dados de exemplo (seed-dev.sql)
```

## Convenções da API

- **JSON em camelCase.** Enums trafegam como texto em minúsculas (ex.: `"camisetas"`, `"em_preparo"`).
- **Autenticação:** header `Authorization: Bearer <token>`, com o token obtido em `/auth/login` ou
  `/auth/cadastro` (validade padrão de 8 horas).
- **Papéis:** `jovem` (padrão de todo cadastro) e `admin` (definido direto no banco).
- **Erros:** formato ProblemDetails; o campo `title` traz um código estável
  (ex.: `ESTOQUE_INSUFICIENTE`) e `detail` a mensagem. Erros de validação retornam `400` com os
  campos inválidos em `errors`.
- **Atualizações parciais (`PATCH`):** só os campos enviados (não nulos) são alterados.

| Status | Significado |
|---|---|
| `200` | Sucesso com corpo |
| `204` | Sucesso sem corpo |
| `400` | Corpo inválido (`errors`) ou `TOKEN_INVALIDO` |
| `401` | Sem token / token inválido ou expirado / `CREDENCIAIS_INVALIDAS` |
| `403` | Sem permissão (rota de admin ou `ACESSO_NEGADO`) |
| `404` | Recurso não encontrado (`*_NAO_ENCONTRADO`) |
| `409` | Conflito de regra de negócio (e-mail em uso, estoque, vagas, status final) |

## Endpoints

Legenda de acesso: 🌐 público · 🔒 autenticado · 🛡️ somente admin.
Detalhes de corpo, parâmetros, exemplos e respostas de cada um em [`docs/API.md`](docs/API.md).

### Health

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| GET | `/health` | 🌐 | Retorna `{ "status": "healthy", "database": "connected" }` se a API e o banco estiverem ok; `503` se o banco estiver fora. |

### Auth

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| POST | `/auth/cadastro` | 🌐 | Cria conta (`nome`, `email`, `senha` com 8+ caracteres) com papel `jovem` e já devolve `{ usuario, token }`. `409 EMAIL_EM_USO` se o e-mail já existir. |
| POST | `/auth/login` | 🌐 | Recebe `email` e `senha`, devolve `{ usuario, token }`. `401 CREDENCIAIS_INVALIDAS` se não bater. |
| GET | `/auth/me` | 🔒 | Devolve o usuário do token (`id`, `nome`, `email`, `telefone`, `papel`). |
| PATCH | `/auth/perfil` | 🔒 | Atualiza `nome`, `email` e/ou `telefone` do usuário logado (todos opcionais). |
| POST | `/auth/recuperar-senha` | 🌐 | Recebe `email` e envia link de redefinição (válido por 1 h). Sempre `204`, mesmo se o e-mail não existir. |
| POST | `/auth/redefinir-senha` | 🌐 | Recebe `token` (do link) e `novaSenha`; troca a senha. Token é de uso único. `400 TOKEN_INVALIDO` se inválido/expirado. |

### Usuários

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| GET | `/usuarios/{id}` | 🔒 | Devolve um usuário. Usuário comum só consulta a si mesmo (`403 ACESSO_NEGADO` caso contrário); admin consulta qualquer um. |

### Produtos

Cada produto tem `variacoes` (tamanho + cor + estoque); `tamanhos` e `cores` são derivados delas.

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| GET | `/produtos` | 🌐 | Lista produtos. Query opcional: `categoria` (`camisetas`, `moletons`, `acessorios`) e `busca` (trecho do nome, sem diferenciar maiúsculas). |
| GET | `/produtos/destaques` | 🌐 | Lista produtos com `destaque = true`. |
| GET | `/produtos/{id}` | 🌐 | Detalhe do produto, incluindo variações e estoque. |
| POST | `/produtos` | 🛡️ | Cria produto: `nome`, `categoria`, `preco` (> 0), `descricao`, `fotos?`, `destaque?`, `variacoes` (1+). |
| PATCH | `/produtos/{id}` | 🛡️ | Atualiza campos enviados. Enviar `variacoes` **substitui** todas as variações. |
| DELETE | `/produtos/{id}` | 🛡️ | Remove o produto (`204`). Pedidos antigos mantêm o snapshot do produto. |

### Eventos

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| GET | `/eventos` | 🌐 | Lista eventos. `?apenasFuturos=true` filtra os que ainda não aconteceram. |
| GET | `/eventos/{id}` | 🌐 | Detalhe do evento, incluindo `vagasRestantes`. |
| GET | `/eventos/{id}/vagas-restantes` | 🌐 | Devolve só `{ "vagasRestantes": n }`. |
| POST | `/eventos` | 🛡️ | Cria evento: `titulo`, `descricao`, `dataHora`, `local`, `preco` (>= 0), `vagasTotais` (>= 1), `foto`. |
| PATCH | `/eventos/{id}` | 🛡️ | Atualiza campos enviados. Não permite `vagasTotais` abaixo das inscrições confirmadas (`409`). |
| DELETE | `/eventos/{id}` | 🛡️ | Remove o evento (`204`). Bloqueado se houver inscrições confirmadas (`409`). |

### Inscrições

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| POST | `/eventos/{eventoId}/inscricoes` | 🔒 | Inscreve o usuário logado (sem corpo). Devolve `{ resultado, inscricao }` com `resultado` = `criada`, `ja_inscrito` ou `esgotado`. Seguro contra overbooking. |
| GET | `/usuarios/me/inscricoes` | 🔒 | Lista as inscrições do usuário logado (mais recentes primeiro). |
| GET | `/eventos/{eventoId}/inscricoes` | 🛡️ | Lista as inscrições de um evento. |
| PATCH | `/inscricoes/{id}/cancelar` | 🔒 | Cancela a inscrição (dono ou admin), liberando a vaga. |

### Pedidos

| Método | Rota | Acesso | O que faz |
|---|---|---|---|
| POST | `/pedidos` | 🔒 | Checkout: `itens` (`produtoId`, `tamanho`, `cor`, `quantidade`), `formaEntrega` (`retirada`/`entrega`) e `endereco` (obrigatório em `entrega`). Debita o estoque atomicamente e cria o pedido como `pago`. `409 ESTOQUE_INSUFICIENTE` se faltar estoque. |
| GET | `/usuarios/me/pedidos` | 🔒 | Lista os pedidos do usuário logado (mais recentes primeiro). |
| GET | `/pedidos` | 🛡️ | Lista todos os pedidos. |
| PATCH | `/pedidos/{id}/avancar-status` | 🛡️ | Avança o status: `pago` → `em_preparo` → `retirado` (retirada) ou `entregue` (entrega). `409` se já estiver em estado final. |

## OpenAPI

O arquivo [`docs/openapi.json`](docs/openapi.json) é gerado a partir dos metadados dos endpoints
(resumos, descrições, tipos de resposta e segurança Bearer). Depois de alterar endpoints ou DTOs,
regenere com:

```powershell
./scripts/gerar-openapi.ps1
```
