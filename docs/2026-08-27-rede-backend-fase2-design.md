# Design — Fase 2 (Auth & Usuários)

Este documento detalha, no nível de código, a Fase 2 já descrita em
[`2026-08-26-rede-backend-design.md`](./2026-08-26-rede-backend-design.md)
(seção 9). Aquele documento definiu a arquitetura geral do backend; este
aqui especifica entidades, endpoints, fluxo de recuperação de senha e
estratégia de testes desta fase — a primeira a introduzir entidades de
negócio reais sobre a fundação construída na Fase 1.

Decisões validadas com o usuário para esta fase: provedor de e-mail
**Resend**. Todo o resto (banco, arquitetura em camadas, JWT, hashing de
senha) já estava fixado pela Fase 1 e é reaproveitado sem mudança.

---

## 1. Escopo

Dentro: entidade `Usuario`, os 6 endpoints de auth do doc de requisitos
(seção 2) + o fluxo completo de recuperação de senha (seção 6 do design
geral), harness de testes de integração via `WebApplicationFactory` +
Testcontainers (recomendação da revisão final da Fase 1, tratada aqui
como a primeira entrega desta fase, não uma tarefa solta no meio).

Fora: qualquer coisa de Produtos/Eventos/Pedidos (fases futuras); página
Angular de "redefinir senha" (é frontend, fora deste repositório);
qualquer seed de contas de desenvolvimento (não pedido, não incluído —
se quiser depois, é uma extensão pequena e isolada).

---

## 2. Entidades e persistência

```
Usuario {
  Id: Guid
  Nome: string
  Email: string        // índice único
  Telefone: string?
  Papel: Papel          // enum: Jovem, Admin — salvo como string no banco
  SenhaHash: string
}

PasswordResetToken {
  Id: Guid
  UsuarioId: Guid        // FK -> Usuario
  TokenHash: string      // SHA-256 do token, nunca o token puro
  ExpiraEm: DateTime      // UTC, criado + 1h
  UsadoEm: DateTime?      // UTC, null até ser consumido
}
```

- `SenhaHash` nunca aparece em nenhum DTO de resposta — reforçado por um
  `UsuarioDto` (`Id`, `Nome`, `Email`, `Telefone`, `Papel`) totalmente
  separado da entidade EF, igual ao padrão já usado no doc de requisitos.
- **Por que hash separado para o token de reset, e não `IPasswordHasher`:**
  `PasswordHasher<T>` é deliberadamente lento (PBKDF2 com milhares de
  iterações) para dificultar força bruta contra senhas de baixa entropia
  digitadas por humanos. Um token de reset já nasce com alta entropia
  (32 bytes aleatórios via `RandomNumberGenerator`) — usar o hasher lento
  nele só adicionaria latência sem ganho de segurança. Hash: SHA-256
  simples (`Convert.ToHexString(SHA256.HashData(...))`) do token bruto.
- Primeira migration real do projeto: `dotnet ef migrations add InitialAuth`
  (a partir daqui `RedeStoreDbContext` deixa de estar vazio).

---

## 3. Camada de acesso a dados (por que existe um repositório aqui)

A Fase 1 fixou o grafo de dependências: `Application` depende só de
`Domain`, nunca de `Infrastructure`. Como os serviços de aplicação
(`AuthService`, `UsuarioService`) precisam buscar/persistir `Usuario` e
`PasswordResetToken` sem poder referenciar `RedeStoreDbContext`
diretamente (que mora em `Infrastructure`), interfaces de repositório em
`Application.Common` — implementadas em `Infrastructure.Persistence` —
são estruturalmente necessárias aqui, não uma abstração por
"boas práticas" genéricas:

```csharp
public interface IUsuarioRepository
{
    Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct);
    Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Usuario usuario, CancellationToken ct);
    Task AtualizarAsync(Usuario usuario, CancellationToken ct);
}

public interface IPasswordResetTokenRepository
{
    Task AdicionarAsync(PasswordResetToken token, CancellationToken ct);
    Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct);
    Task AtualizarAsync(PasswordResetToken token, CancellationToken ct);
}
```

Nada além destes métodos — sem `IRepository<T>` genérico, sem
`IUnitOfWork` (cada método de escrita já chama `SaveChangesAsync`
internamente; não há nenhuma transação multi-agregado nesta fase).

---

## 4. Endpoints

| Método | Rota | Auth | Body/Params | Erros de negócio |
|---|---|---|---|---|
| POST | `/auth/cadastro` | pública | `{ nome, email, senha }` | `EMAIL_EM_USO` (409) |
| POST | `/auth/login` | pública | `{ email, senha }` | `CREDENCIAIS_INVALIDAS` (401) |
| GET | `/auth/me` | autenticado | — | — |
| PATCH | `/auth/perfil` | autenticado | `{ nome?, email?, telefone? }` | `EMAIL_EM_USO` (409) se o novo e-mail já existir em outra conta |
| GET | `/usuarios/:id` | autenticado | — | `ACESSO_NEGADO` (403) se não for o próprio id nem admin |
| POST | `/auth/recuperar-senha` | pública | `{ email }` | — (sempre 204, mesmo se e-mail não existir) |
| POST | `/auth/redefinir-senha` | pública | `{ token, novaSenha }` | `TOKEN_INVALIDO` (400) se inexistente/expirado/já usado |

Todas retornam `{ usuario: UsuarioDto, token: string }` nos casos de
cadastro/login (igual ao contrato já documentado); `/auth/me` e
`/usuarios/:id` retornam só `UsuarioDto`.

Novas subclasses de `DomainException` (Fase 1 já define a base):
`EmailEmUsoException`, `CredenciaisInvalidasException`,
`TokenInvalidoException`, `AcessoNegadoException` — cada uma só com
`Codigo`/`StatusCode`, sem lógica própria.

### Validação de request (FluentValidation, decidido na Fase 1, usado pela primeira vez aqui)

Um `IEndpointFilter` genérico (`ValidationFilter<T>`) roda o
`IValidator<T>` registrado antes do handler e devolve
`Results.ValidationProblem(...)` (400) se inválido — reaproveitável por
todo endpoint futuro, não específico de auth. Regras (herdadas dos forms
do frontend, já documentadas): `nome` ≥ 2 chars, `email` formato válido,
`senha` ≥ 8 chars.

---

## 5. Fluxo de recuperação de senha (Resend)

`IEmailSender` (interface genérica, já prevista no design geral):

```csharp
public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct);
}
```

`ResendEmailSender` (`Infrastructure/Email/`) chama a API HTTP do Resend
diretamente (`POST https://api.resend.com/emails`, `Authorization: Bearer
{ApiKey}`, corpo `{ from, to, subject, html }`) via `HttpClient` nomeado —
sem depender do SDK oficial, para não travar a nenhuma versão de pacote
específica numa API REST simples e estável. `ApiKey` e endereço `from`
via `dotnet user-secrets`/config, nunca hardcoded.

`AuthService` monta o e-mail (assunto e HTML ficam na camada de
aplicação, não no sender — o sender só sabe transportar). O link aponta
para `{Frontend:ResetPasswordUrl}?token={token}`, com
`Frontend:ResetPasswordUrl` configurável (`http://localhost:4200/redefinir-senha`
em dev) — **a página Angular ainda não existe, isso é uma dependência a
resolver no frontend, fora deste plano**.

Fluxo:
1. `POST /auth/recuperar-senha` — se o e-mail existir, gera token (32
   bytes aleatórios, base64url), salva `TokenHash` + `ExpiraEm = now+1h`,
   envia e-mail. Sempre responde 204, independente do resultado.
2. `POST /auth/redefinir-senha` — hash do token recebido, busca por
   `TokenHash`; rejeita (`TOKEN_INVALIDO`) se não encontrado, expirado, ou
   `UsadoEm != null`; caso válido, atualiza `SenhaHash` do usuário via
   `IPasswordHasher`, marca `UsadoEm = now`.

---

## 6. Testes — harness primeiro, não no meio da fase

A revisão final da Fase 1 recomendou explicitamente montar o harness de
`WebApplicationFactory<Program>` + Testcontainers como a **primeira**
entrega desta fase, não uma tarefa espalhada depois. Isso vira a Task 1
do plano de implementação.

- **Harness:** uma classe base (`IntegrationTestBase`/`ApiFactory`) que
  sobe um Postgres real via Testcontainers, configura o
  `WebApplicationFactory` para usar essa connection string, roda
  `Database.MigrateAsync()` antes dos testes, e substitui `IEmailSender`
  por um `FakeEmailSender` (captura os e-mails enviados numa lista em
  memória, sem chamar a API real do Resend) via
  `services.Replace(ServiceDescriptor.Singleton<IEmailSender, FakeEmailSender>())`.
- **Testes end-to-end sobre esse harness:** cadastro (sucesso +
  `EMAIL_EM_USO`), login (sucesso + `CREDENCIAIS_INVALIDAS`), acessar
  `/auth/me` com o token do login, `/usuarios/:id` (próprio OK, de outro
  usuário sem ser admin → 403), atualizar perfil, e o fluxo completo de
  recuperação de senha (recuperar → extrair token do `FakeEmailSender` →
  redefinir → logar com a senha nova) — o mesmo teste também cobre
  `TOKEN_INVALIDO` para um token usado duas vezes.
- **Unitários:** validators do FluentValidation, e as duas exceções de
  negócio mais específicas (`AuthService` decidindo `EMAIL_EM_USO` vs
  sucesso) isoladas com o repositório mockado.

---

## 7. Definition of Done

Fluxo cadastro → login → `/auth/me` → `/auth/perfil` → `/usuarios/:id`
(próprio e como admin) → esqueci a senha → redefinir com token → logar
com a senha nova, tudo provado por testes de integração reais (Postgres
via Testcontainers, e-mail capturado via fake, nunca mockado no nível de
"sempre retorna true"). Nenhuma senha ou token em texto puro persistido.
Nenhuma chamada real à API do Resend durante os testes.
