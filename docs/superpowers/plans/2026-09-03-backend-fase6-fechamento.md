# Backend REDE — Fase 6 (Fechamento) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out the backend roadmap: cover the remaining cross-feature integration gaps, run a systematic authorization/security audit against the requirements doc's role table, and ship a production-ready deploy artifact (Dockerfile + environment variable guide) — with no new domain features and no re-architecting.

**Architecture:** No new entities, services, or endpoints. Task 1 adds integration tests that exercise more than one feature's endpoints in a single scenario (something no existing phase's own end-to-end suite does, since each phase's tests only call other features' endpoints as setup helpers, never as part of the assertion). Task 2 is an audit pass producing a written report plus, only if a real gap is found, the minimal code fix and a regression test for it — the same "review then fix if needed" shape Fase 5's final review used. Task 3 adds a multi-stage production `Dockerfile`, one small addition to `Program.cs` (apply pending EF Core migrations automatically at startup, since there is no separate migration-runner artifact in this project), and a deployment guide documenting every environment variable the container needs.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, xUnit + Testcontainers.PostgreSql (already wired), Docker (multi-stage build, `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`). No new NuGet packages needed for this phase.

**Spec:** `docs/2026-08-26-rede-backend-design.md` (section 9 "Fase 6 — Fechamento", section 4 "Autenticação e segurança", section 8 "Ambiente de desenvolvimento") and `docs/2026-08-26-rede-backend-requisitos.md` (section 3 "DELETE /produtos/:id — decisão de negócio", section 8 "Resumo de autorização por papel").

## Global Constraints

- Target framework `net10.0` for every project (already set). Fixed dependency graph (unchanged): `Domain` → nothing; `Application` → `Domain` only; `Infrastructure` → `Application` + `Domain`; `Api` → `Application` + `Infrastructure`.
- No mocking library. Unit tests needing a fake repository use small hand-rolled in-memory classes — not Moq/NSubstitute (same rule as every prior phase). This phase adds no new unit tests, but the rule still binds if any test doubles are touched.
- Integration tests share one Postgres container/database across the whole run via `ICollectionFixture` (`IntegrationTestCollection`) — tests must not collide, use `Guid.NewGuid()`-suffixed values wherever a test asserts on a specific row, exactly like every existing integration test file in this repo.
- Every existing integration test file follows the same two helper conventions verbatim — reuse them, do not invent a variant: `CriarClienteAdminAsync()` (cadastro → promote to `Papel.Admin` via `IUsuarioRepository` in a DI scope → login → new `HttpClient` with the bearer token) and `CriarClienteJovemAsync()` (cadastro → login → new `HttpClient` with the bearer token). Both are duplicated per-file across every phase's E2E test file in this codebase (`ProdutoFlowEndToEndTests.cs`, `EventoFlowEndToEndTests.cs`, `InscricaoFlowEndToEndTests.cs`, `PedidoFlowEndToEndTests.cs`) — this phase's new test file follows the same duplication convention, not a shared base class (the repo has never introduced one; do not introduce one unilaterally in this phase).
- `RedeStoreDbContext.Database.MigrateAsync()` is already called once by `ApiFactory.InitializeAsync()` (`tests/RedeStore.IntegrationTests/ApiFactory.cs:50`) for every test run. Task 3 adds a second `MigrateAsync()` call inside `Program.cs` itself (so a real deployed container also gets its schema applied automatically, since this project has no separate migration-runner step). Calling `MigrateAsync()` twice in the same process against an already-migrated database is safe and a no-op on the second call — EF Core tracks applied migrations in the `__EFMigrationsHistory` table and skips anything already applied. Do not remove or guard against this "double call" — it is expected and does not need an environment check.
- `GlobalExceptionHandler` (`src/RedeStore.Api/Middleware/GlobalExceptionHandler.cs`) needs no changes in this phase — it already maps any `DomainException` polymorphically and everything else to a generic 500 `ERRO_INTERNO`. If Task 2's audit finds a route that needs a *new* authorization check, express it with `AcessoNegadoException` (existing, `src/RedeStore.Domain/Exceptions/AcessoNegadoException.cs`, `"ACESSO_NEGADO"`, 403) — do not invent a new exception type for this phase unless the audit finds something that genuinely doesn't fit that shape.
- The current route inventory (verified against the running code before this plan was written — Task 2 re-verifies each of these against `requisitos` section 8, this list is the starting point, not a substitute for reading the actual route+service code):

  | Route | Policy today |
  |---|---|
  | `POST /auth/cadastro` | pública |
  | `POST /auth/login` | pública |
  | `GET /auth/me` | `.RequireAuthorization()` |
  | `PATCH /auth/perfil` | `.RequireAuthorization()`, self-only (id from JWT `sub` claim, never from the request body) |
  | `POST /auth/recuperar-senha` | pública, always `204` regardless of whether the e-mail exists |
  | `POST /auth/redefinir-senha` | pública |
  | `GET /usuarios/{id}` | `.RequireAuthorization()`, self-or-admin enforced in `AuthService` (`AcessoNegadoException` otherwise) |
  | `GET /produtos`, `/produtos/destaques`, `/produtos/{id}` | pública |
  | `POST /produtos`, `PATCH /produtos/{id}`, `DELETE /produtos/{id}` | `.RequireAuthorization("Admin")` |
  | `GET /eventos`, `/eventos/{id}`, `/eventos/{id}/vagas-restantes` | pública |
  | `POST /eventos`, `PATCH /eventos/{id}`, `DELETE /eventos/{id}` | `.RequireAuthorization("Admin")` |
  | `POST /eventos/{id}/inscricoes` | `.RequireAuthorization()` |
  | `GET /usuarios/me/inscricoes` | `.RequireAuthorization()` |
  | `GET /eventos/{id}/inscricoes` | `.RequireAuthorization("Admin")` |
  | `PATCH /inscricoes/{id}/cancelar` | `.RequireAuthorization()`, dono-or-admin enforced in `InscricaoService` (`AcessoNegadoException` otherwise) |
  | `POST /pedidos` | `.RequireAuthorization()` |
  | `GET /usuarios/me/pedidos` | `.RequireAuthorization()` |
  | `GET /pedidos` | `.RequireAuthorization("Admin")` |
  | `PATCH /pedidos/{id}/avancar-status` | `.RequireAuthorization("Admin")` |
  | `GET /health` | pública |

---

## Task 1: Cross-Feature Integration Tests

**Files:**
- Create: `tests/RedeStore.IntegrationTests/CrossFeature/CrossFeatureFlowEndToEndTests.cs`

**Interfaces:**
- Consumes: every existing `IPedidoService`/`IProdutoService`/`IEventoService`/`IInscricaoService`/`IAuthService` HTTP route (Fases 2-5, unchanged) — no new production interfaces.
- Produces: nothing new for later tasks — Task 2 and Task 3 do not depend on this task's output.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/CrossFeature/CrossFeatureFlowEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.CrossFeature;

[Collection(IntegrationTestCollection.Name)]
public class CrossFeatureFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public CrossFeatureFlowEndToEndTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> CriarClienteAdminAsync()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Admin", email, "senha12345"));

        using (var scope = _factory.Services.CreateScope())
        {
            var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
            var usuario = await usuarioRepositorio.BuscarPorEmailAsync(email, CancellationToken.None);
            usuario!.Papel = Papel.Admin;
            await usuarioRepositorio.AtualizarAsync(usuario, CancellationToken.None);
        }

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return clienteAdmin;
    }

    private async Task<HttpClient> CriarClienteJovemAsync()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return cliente;
    }

    private async Task<Guid> CriarProdutoComEstoqueAsync(HttpClient clienteAdmin, int estoque, decimal preco = 79.90m)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: $"Camiseta {Guid.NewGuid()}",
            Categoria: "camisetas",
            Preco: preco,
            Descricao: "Camiseta de teste",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: false,
            Variacoes: [new VariacaoRequest("M", "Preto", estoque)]));
        var produto = await response.Content.ReadFromJsonAsync<ProdutoDto>();
        return produto!.Id;
    }

    private async Task<Guid> CriarEventoAsync(HttpClient clienteAdmin, int vagasTotais)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/eventos", new CriarEventoRequest(
            Titulo: $"Retiro {Guid.NewGuid()}",
            Descricao: "Retiro anual",
            DataHora: DateTime.UtcNow.AddDays(30),
            Local: "Sítio da Rede",
            Preco: 50m,
            VagasTotais: vagasTotais,
            Foto: "https://exemplo.com/retiro.jpg"));
        var evento = await response.Content.ReadFromJsonAsync<EventoDto>();
        return evento!.Id;
    }

    [Fact]
    public async Task FluxoDeUsuarioAtravesDeTodasAsFeatures_ComprarProdutoEInscreverEmEvento_ApareceCorretamenteParaOUsuarioEParaOAdmin()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 5);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarPedidoResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "retirada", null));
        criarPedidoResponse.EnsureSuccessStatusCode();
        var pedidoCriado = await criarPedidoResponse.Content.ReadFromJsonAsync<PedidoDto>();

        var inscreverResponse = await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        inscreverResponse.EnsureSuccessStatusCode();
        var resultadoInscricao = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        Assert.Equal("criada", resultadoInscricao!.Resultado);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(meusPedidos!, p => p.Id == pedidoCriado!.Id);

        var minhasInscricoesResponse = await clienteJovem.GetAsync("/usuarios/me/inscricoes");
        var minhasInscricoes = await minhasInscricoesResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Contains(minhasInscricoes!, i => i.EventoId == eventoId);

        var todosOsPedidosResponse = await clienteAdmin.GetAsync("/pedidos");
        var todosOsPedidos = await todosOsPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(todosOsPedidos!, p => p.Id == pedidoCriado!.Id);

        var inscricoesDoEventoResponse = await clienteAdmin.GetAsync($"/eventos/{eventoId}/inscricoes");
        var inscricoesDoEvento = await inscricoesDoEventoResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Contains(inscricoesDoEvento!, i => i.EventoId == eventoId);
    }

    [Fact]
    public async Task DeleteProduto_ComPedidoExistenteReferenciandoOProduto_PermiteDeleteEPreservaOSnapshotDoPedido()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10, preco: 79.90m);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarPedidoResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 2)], "retirada", null));
        criarPedidoResponse.EnsureSuccessStatusCode();
        var pedidoCriado = await criarPedidoResponse.Content.ReadFromJsonAsync<PedidoDto>();
        var nomeOriginal = pedidoCriado!.Itens[0].Nome;
        var precoOriginal = pedidoCriado.Itens[0].PrecoUnitario;
        var fotoOriginal = pedidoCriado.Itens[0].FotoUrl;
        var valorTotalOriginal = pedidoCriado.ValorTotal;

        var deletarResponse = await clienteAdmin.DeleteAsync($"/produtos/{produtoId}");
        Assert.Equal(HttpStatusCode.NoContent, deletarResponse.StatusCode);

        var produtoDepoisResponse = await _client.GetAsync($"/produtos/{produtoId}");
        Assert.Equal(HttpStatusCode.NotFound, produtoDepoisResponse.StatusCode);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        meusPedidosResponse.EnsureSuccessStatusCode();
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        var pedidoAposDelete = meusPedidos!.Single(p => p.Id == pedidoCriado.Id);
        Assert.Equal(nomeOriginal, pedidoAposDelete.Itens[0].Nome);
        Assert.Equal(precoOriginal, pedidoAposDelete.Itens[0].PrecoUnitario);
        Assert.Equal(fotoOriginal, pedidoAposDelete.Itens[0].FotoUrl);
        Assert.Equal(valorTotalOriginal, pedidoAposDelete.ValorTotal);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail or pass for the right reason**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~CrossFeatureFlowEndToEndTests"`
Expected: both tests pass immediately, since this task wires no new production code — it only proves behavior that Fases 2-5 already built but never exercised together in one scenario. If either test fails, that is a real, previously-undetected cross-feature bug: stop and investigate it via `superpowers:systematic-debugging` before touching the test — do not weaken the assertions to make it pass.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions (this task adds tests only, no production code).

- [ ] **Step 4: Commit**

```bash
git add tests/RedeStore.IntegrationTests/CrossFeature/
git commit -m "Add cross-feature integration tests covering Fase 6 Definition of Done"
```

---

## Task 2: Security & Authorization Review

**Files:**
- Create: `docs/2026-09-03-fase6-revisao-seguranca.md`
- Modify: only if the audit finds a real gap — the specific file(s) implementing the fix, plus a regression test in the corresponding existing test file (`tests/RedeStore.UnitTests/...` or `tests/RedeStore.IntegrationTests/...`, following that file's existing conventions).

**Interfaces:**
- Consumes: every route and service in the codebase (read-only unless a gap is found).
- Produces: `docs/2026-09-03-fase6-revisao-seguranca.md`, a written audit report. Task 3 does not depend on this task's output.

This task is an audit, not a fixed code change — write the actual findings into the report, not a template. Work through every item below and record a verdict for each one (✅ confirmed correct, with the file:line that proves it — or ❌ gap found, with what was wrong and how it was fixed) rather than leaving any item unchecked.

- [ ] **Step 1: Route-by-route authorization audit**

For every route in the Global Constraints table above, do two things and record both in the report:

1. Confirm the endpoint-level policy in the route mapping file (`src/RedeStore.Api/Endpoints/*.cs`) matches the table (this is a direct read, not a test run).
2. For every route the table marks as needing more than a bare policy check — self-only or dono-or-admin — read the service method it calls and confirm the check is really there and really throws `AcessoNegadoException` (or an equivalent 403) on a mismatch, not just that the table claims it does. The three routes already spot-verified while this plan was written (do not skip re-confirming them, just start from the answer already given):
   - `PATCH /auth/perfil` — `AuthService.AtualizarPerfilAsync`, called with an `id` parsed from the JWT `sub` claim in `AuthEndpoints.cs:36`, never from the request body. Confirm `AtualizarPerfilRequest` (`src/RedeStore.Application/Auth/Dtos/AtualizarPerfilRequest.cs`) has no `id`/`usuarioId` field that could target another account.
   - `GET /usuarios/{id}` — `AuthService.ObterPorIdAsync` or equivalent, confirm the self-or-admin check and its `AcessoNegadoException` throw are present (`src/RedeStore.Application/Auth/AuthService.cs`, near line 82 as of this writing).
   - `PATCH /inscricoes/{id}/cancelar` — `InscricaoService.CancelarAsync(inscricaoId, idUsuarioLogado, ehAdmin, ct)` (`src/RedeStore.Application/Inscricoes/InscricaoService.cs`, near line 68 as of this writing), confirm the `inscricao.UsuarioId != idUsuarioLogado && !ehAdmin` check.

- [ ] **Step 2: Data-exposure audit**

Check and record each of these:

1. `UsuarioDto` (`src/RedeStore.Application/Auth/Dtos/UsuarioDto.cs`) never includes `SenhaHash` or any password-derived field — confirm by reading its record definition, and confirm every endpoint that returns a user (`/auth/cadastro`, `/auth/login`, `/auth/me`, `PATCH /auth/perfil`, `GET /usuarios/{id}`) returns this DTO type, never the `Usuario` entity directly.
2. `POST /auth/recuperar-senha` returns `204` unconditionally, whether or not the e-mail exists — confirm by reading `AuthService.RecuperarSenhaAsync` (`src/RedeStore.Application/Auth/AuthService.cs`, near line 116 as of this writing): it must return early on a missing user with no distinguishable side effect (timing, response body, or status code) between "e-mail exists" and "e-mail doesn't exist". If you find a timing difference is even theoretically observable (e.g. the found-user path does meaningfully more async work than the not-found path), note it in the report as a Minor finding — do not attempt to build constant-time padding for it in this task, that is out of scope; just document it.
3. `PasswordResetToken.TokenHash` — confirm the raw reset token is never persisted or logged in plaintext anywhere in `AuthService`, only its SHA-256 hash (`src/RedeStore.Application/Auth/AuthService.cs`, near line 126 as of this writing uses `SHA256.HashData` before persisting).
4. `ProblemDetails` responses from `GlobalExceptionHandler` (`src/RedeStore.Api/Middleware/GlobalExceptionHandler.cs`) never leak a raw exception message or stack trace for non-`DomainException` failures — confirm the 500 path always uses the fixed string `"Ocorreu um erro inesperado."`, never `exception.Message` or `exception.ToString()`, and that the full exception detail only goes to the server-side logger (`logger.LogError`), never the HTTP response body.
5. CORS policy (`Program.cs`, `FrontendCorsPolicy`) is scoped to a single configurable origin (`Cors:AllowedOrigin`), not a wildcard `AllowAnyOrigin()` — confirm by reading the policy registration.
6. JWT validation (`src/RedeStore.Infrastructure/Auth/JwtTokenValidationParametersFactory.cs` or equivalent) validates issuer, audience, lifetime, and signing key — not just the signature — so an expired or wrong-audience token is genuinely rejected. If a unit or integration test already proves this (check `tests/` for an expired-token or wrong-signature test), cite it; if none exists, add one confirming an expired token is rejected with `401` (a minimal integration test posting a request with a token whose `exp` claim is already in the past, following the existing bearer-token test pattern in any `*FlowEndToEndTests.cs` file).

- [ ] **Step 3: If any real gap was found in Step 1 or Step 2, fix it**

Apply the minimal fix — do not restructure surrounding code. Add a regression test in the existing test file for that feature (unit test if the check belongs in an `Application` service already covered by hand-rolled fakes, integration test if it's route-level), following that file's existing helper/assertion conventions exactly, the same way every prior phase's fixes did. Run the focused test for the fix, then the full suite once.

If Step 2.6 requires adding the expired-token test, that test itself IS the fix for this step (there is no separate "production code" change needed if JWT validation is already correctly configured — the gap being closed is missing *test coverage* proving it, not missing *behavior*).

- [ ] **Step 4: Write the audit report**

Create `docs/2026-09-03-fase6-revisao-seguranca.md` with:
- A route-by-route table (mirroring the Global Constraints table above) with a ✅/❌ column and a one-line note per row citing the file:line evidence.
- A "Data exposure" section covering the 6 items from Step 2, each with a verdict and evidence.
- A "Findings" section listing anything that was NOT already correct: what was wrong, what was changed, which commit fixed it, which test proves it. If nothing was wrong, say so explicitly — "no gaps found, N routes and 6 data-exposure checks verified" — do not leave this section implicit.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: everything passes (including any new regression test from Step 3).

- [ ] **Step 6: Commit**

```bash
git add docs/2026-09-03-fase6-revisao-seguranca.md
# plus any files touched in Step 3, if a gap was found and fixed
git commit -m "Add Fase 6 security and authorization review"
```

(If Step 3 produced a fix, commit it separately from the report with its own descriptive message, mirroring how every prior phase separated "add tests/feature" commits from "fix review finding" commits — do not squash the two into one commit.)

---

## Task 3: Production Dockerfile, Automatic Migrations, and Deployment Guide

**Files:**
- Create: `Dockerfile` (repo root)
- Create: `.dockerignore` (repo root)
- Modify: `src/RedeStore.Api/Program.cs`
- Create: `docs/deploy.md`

**Interfaces:**
- Consumes: `RedeStoreDbContext` (unchanged), all existing `Configure<TOptions>`/`GetConnectionString` calls already in `Program.cs` (unchanged) — this task only adds one new block of startup code and two new files, it does not touch any DI registration.
- Produces: a buildable Docker image and a documented environment variable contract. No later task depends on this.

- [ ] **Step 1: Add automatic migration application on startup**

Open `src/RedeStore.Api/Program.cs`. Immediately after the line `var app = builder.Build();` and before `app.UseExceptionHandler();`, insert:

```csharp
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RedeStoreDbContext>();
    await dbContext.Database.MigrateAsync();
}
```

No new `using` statement is needed — `RedeStore.Infrastructure.Persistence` (for `RedeStoreDbContext`) is already imported, and `CreateScope`/`GetRequiredService` resolve via the Web SDK's implicit usings (the same reason the existing `builder.Services.AddScoped<...>()` calls above don't need an explicit `Microsoft.Extensions.DependencyInjection` using).

This makes a freshly-deployed container self-sufficient: it applies any pending migration before accepting traffic, matching what `ApiFactory.InitializeAsync()` already does for every test run (`tests/RedeStore.IntegrationTests/ApiFactory.cs:50`) — this project has no separate migration-runner artifact, so the API itself is what applies its own schema.

- [ ] **Step 2: Verify no regressions from the migration change**

Run: `dotnet test`
Expected: everything passes. (`ApiFactory`'s own `MigrateAsync()` call still runs too — calling it twice against an already-migrated database is a documented no-op, see Global Constraints.)

- [ ] **Step 3: Write the Dockerfile**

Create `Dockerfile` at the repo root:

```dockerfile
# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/RedeStore.Domain/RedeStore.Domain.csproj src/RedeStore.Domain/
COPY src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Application/
COPY src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj src/RedeStore.Infrastructure/
COPY src/RedeStore.Api/RedeStore.Api.csproj src/RedeStore.Api/
RUN dotnet restore src/RedeStore.Api/RedeStore.Api.csproj

COPY src/ src/
RUN dotnet publish src/RedeStore.Api/RedeStore.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "RedeStore.Api.dll"]
```

`USER $APP_UID` is the non-root user already baked into the `mcr.microsoft.com/dotnet/aspnet` image (the standard .NET 8+ container pattern) — do not run the container as root. Copying only the four `.csproj` files before the full `src/` copy lets `dotnet restore` be cached by Docker across rebuilds that only change source, not dependencies.

- [ ] **Step 4: Write the .dockerignore**

Create `.dockerignore` at the repo root:

```
**/bin/
**/obj/
**/.vs/
**/.vscode/
**/.git/
**/.gitignore
docs/
tests/
**/*.md
.superpowers/
Dockerfile
.dockerignore
docker-compose.yml
```

- [ ] **Step 5: Build the image**

Run: `docker build -t redestore-api:fase6-verify .`
Expected: build completes with no errors, both stages succeed.

- [ ] **Step 6: Verify the image actually runs and serves traffic against a real Postgres**

```bash
docker compose -p fase6verify up -d db

docker run --rm -d --name redestore-api-verify \
  --network fase6verify_default \
  -p 8080:8080 \
  -e ConnectionStrings__Default="Host=db;Port=5432;Database=redestore;Username=postgres;Password=postgres" \
  -e Jwt__SigningKey="chave-de-verificacao-com-pelo-menos-32-caracteres" \
  -e Jwt__Issuer="RedeStore.Verify" \
  -e Jwt__Audience="RedeStore.Verify.Clients" \
  -e Resend__ApiKey="chave-fake-para-verificacao" \
  -e Resend__FromEmail="nao-responda@teste.com" \
  -e Frontend__ResetPasswordUrl="http://localhost:4200/redefinir-senha" \
  redestore-api:fase6-verify

for i in $(seq 1 10); do
  resposta=$(curl -s -o /tmp/health-body.json -w "%{http_code}" http://localhost:8080/health || true)
  if [ "$resposta" = "200" ]; then break; fi
  sleep 2
done
echo "HTTP status: $resposta"
cat /tmp/health-body.json

docker stop redestore-api-verify
docker compose -p fase6verify down
```

Expected: `HTTP status: 200` and a body of `{"status":"healthy","database":"connected"}` — this proves the container both booted correctly (no crash on missing config) and applied its own migrations against a real Postgres it had never seen before (Step 1's addition is what makes `database: "connected"` possible without a manual `dotnet ef database update` step first). If the status is anything else, read the container logs (`docker logs redestore-api-verify`) before touching the Dockerfile — the error message will say exactly what's missing (usually a required env var).

- [ ] **Step 7: Write the deployment guide**

Create `docs/deploy.md`:

```markdown
# Guia de Deploy — RedeStore Backend

Este documento descreve como construir e rodar a imagem Docker de produção
da API, e quais variáveis de ambiente ela exige. Nenhum provedor de
hospedagem específico está amarrado aqui — a imagem roda em qualquer
ambiente que aceite um container Docker padrão escutando na porta 8080.

## Build

```bash
docker build -t redestore-api:latest .
```

## Variáveis de ambiente

Todas usam a convenção de dupla-underscore do ASP.NET Core para seções
aninhadas (`Secao:Chave` no `appsettings.json` vira `Secao__Chave` como
variável de ambiente). Nenhuma delas deve ir para um `appsettings.json`
commitado — em produção, todas vêm de variáveis de ambiente (ou do
mecanismo de segredos do seu provedor de hospedagem).

| Variável | Obrigatória | Formato / exemplo | Descrição |
|---|---|---|---|
| `ConnectionStrings__Default` | Sim | `Host=<host>;Port=5432;Database=redestore;Username=<user>;Password=<senha>` | Connection string do PostgreSQL. A aplicação aplica as migrations pendentes automaticamente na inicialização — não é necessário rodar `dotnet ef database update` manualmente antes do deploy. |
| `Jwt__SigningKey` | Sim | string com pelo menos 32 bytes | Chave HMAC-SHA256 usada para assinar e validar os access tokens JWT. A aplicação falha ao iniciar (`ValidateOnStart`) se estiver ausente ou curta demais. |
| `Jwt__Issuer` | Sim | ex: `https://api.redestore.com.br` | Emissor esperado nos tokens JWT. |
| `Jwt__Audience` | Sim | ex: `redestore-frontend` | Audiência esperada nos tokens JWT. |
| `Jwt__ExpirationHours` | Não (padrão: `8`) | inteiro | Validade do access token em horas. |
| `Resend__ApiKey` | Sim | chave de API do Resend | Usada para enviar o e-mail de recuperação de senha. A aplicação falha ao iniciar se estiver ausente. |
| `Resend__FromEmail` | Sim | ex: `nao-responda@redestore.com.br` | Endereço remetente dos e-mails enviados via Resend. |
| `Frontend__ResetPasswordUrl` | Sim | ex: `https://app.redestore.com.br/redefinir-senha` | URL do frontend Angular para onde o link de redefinição de senha aponta. |
| `Cors__AllowedOrigin` | Não (padrão: `http://localhost:4200`) | ex: `https://app.redestore.com.br` | Origem liberada pela policy de CORS. Em produção, deve apontar para o domínio real do frontend, não o padrão de desenvolvimento. |
| `ASPNETCORE_ENVIRONMENT` | Não (padrão: `Production` em um container) | `Production` \| `Development` | Controla exposição do Scalar/OpenAPI (`/scalar`, `/openapi`) — só ficam disponíveis em `Development`. Deixe sem definir (ou `Production`) em produção. |

## Rodando localmente para testar a imagem

```bash
docker compose up -d db   # sobe só o Postgres, via docker-compose.yml já existente

docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Default="Host=host.docker.internal;Port=5432;Database=redestore;Username=postgres;Password=postgres" \
  -e Jwt__SigningKey="troque-por-uma-chave-de-pelo-menos-32-caracteres" \
  -e Jwt__Issuer="RedeStore.Local" \
  -e Jwt__Audience="RedeStore.Local.Clients" \
  -e Resend__ApiKey="sua-chave-resend" \
  -e Resend__FromEmail="nao-responda@exemplo.com" \
  -e Frontend__ResetPasswordUrl="http://localhost:4200/redefinir-senha" \
  redestore-api:latest
```

`GET /health` deve responder `{"status":"healthy","database":"connected"}`.

## Migrations

A imagem aplica automaticamente qualquer migration pendente do EF Core na
inicialização (`RedeStoreDbContext.Database.MigrateAsync()`, chamado antes
do `app.Run()`). Não existe um artefato separado de "migration runner" —
o próprio container, ao subir, garante que o schema do banco está
atualizado antes de aceitar requisições.
```

- [ ] **Step 8: Commit**

```bash
git add Dockerfile .dockerignore src/RedeStore.Api/Program.cs docs/deploy.md
git commit -m "Add production Dockerfile, automatic startup migrations, and deployment guide"
```

---
