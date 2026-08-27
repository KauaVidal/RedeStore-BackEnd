# Backend REDE — Fase 1 (Fundação) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the .NET 10 solution skeleton for the RedeStore backend — projects, Postgres connectivity, JWT auth primitives, global error handling, and API docs — with no business features yet, so Fases 2+ (Auth, Produtos, Eventos, Pedidos) have a working foundation to build on.

**Architecture:** 4-project layered solution (`Api` → `Application`/`Infrastructure` → `Domain`), ASP.NET Core Minimal APIs, EF Core against PostgreSQL, JWT Bearer auth with a single access token (no refresh), `IExceptionHandler`-based global error mapping to `ProblemDetails`.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, `System.IdentityModel.Tokens.Jwt`, `Microsoft.AspNetCore.Identity.PasswordHasher<T>`, xUnit, Testcontainers (Postgres), Scalar (OpenAPI UI), Docker Compose.

**Spec:** `docs/2026-08-26-rede-backend-design.md` (see section 9, "Fase 1 — Fundação") and `docs/2026-08-26-rede-backend-requisitos.md` for domain context.

## Global Constraints

- Target framework for every project: `net10.0` (matches installed SDK `10.0.301`).
- Database: PostgreSQL only — no SQL Server/SQLite code paths.
- No CQRS/MediatR — plain service classes injected via DI (per design section 1/2).
- Dependency graph is fixed: `Domain` depends on nothing; `Application` depends on `Domain`; `Infrastructure` depends on `Application` + `Domain`; `Api` (composition root) depends on `Application` + `Infrastructure`.
- Secrets (connection strings, JWT signing key) live only in `dotnet user-secrets` (dev) or environment variables (prod) — never in any `appsettings*.json` committed to git.
- No business entities (Usuario, Produto, Evento, Pedido, etc.) in this phase — those belong to Fases 2-5. `RedeStoreDbContext` stays empty (zero `DbSet<T>`) until Fase 2.
- All commands below assume the current working directory is the repo root: `C:\Users\Kaka1\OneDrive\Área de Trabalho\redeStore\RedeStore-BackEnd`.

---

## Task 1: Solution & Project Skeleton

**Files:**
- Create: `RedeStore.sln`
- Create: `src/RedeStore.Domain/RedeStore.Domain.csproj`
- Create: `src/RedeStore.Application/RedeStore.Application.csproj`
- Create: `src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj`
- Create: `src/RedeStore.Api/RedeStore.Api.csproj` (+ template-generated `Program.cs`, `appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`)
- Create: `tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj`
- Create: `tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj`
- Create: `docker-compose.yml`
- Create: `.gitignore`

**Interfaces:**
- Produces: the project/reference graph every later task builds on. No code interfaces yet.

- [ ] **Step 1: Create the solution and all projects**

```bash
dotnet new sln -n RedeStore

dotnet new classlib -n RedeStore.Domain -o src/RedeStore.Domain
rm src/RedeStore.Domain/Class1.cs

dotnet new classlib -n RedeStore.Application -o src/RedeStore.Application
rm src/RedeStore.Application/Class1.cs

dotnet new classlib -n RedeStore.Infrastructure -o src/RedeStore.Infrastructure
rm src/RedeStore.Infrastructure/Class1.cs

dotnet new web -n RedeStore.Api -o src/RedeStore.Api

dotnet new xunit -n RedeStore.UnitTests -o tests/RedeStore.UnitTests
rm -f tests/RedeStore.UnitTests/UnitTest1.cs

dotnet new xunit -n RedeStore.IntegrationTests -o tests/RedeStore.IntegrationTests
rm -f tests/RedeStore.IntegrationTests/UnitTest1.cs
```

- [ ] **Step 2: Add every project to the solution**

```bash
dotnet sln RedeStore.sln add \
  src/RedeStore.Domain/RedeStore.Domain.csproj \
  src/RedeStore.Application/RedeStore.Application.csproj \
  src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj \
  src/RedeStore.Api/RedeStore.Api.csproj \
  tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj \
  tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj
```

- [ ] **Step 3: Wire up project references (fixed dependency graph)**

```bash
dotnet add src/RedeStore.Application/RedeStore.Application.csproj reference src/RedeStore.Domain/RedeStore.Domain.csproj

dotnet add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj reference src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Domain/RedeStore.Domain.csproj

dotnet add src/RedeStore.Api/RedeStore.Api.csproj reference src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj

dotnet add tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj reference src/RedeStore.Domain/RedeStore.Domain.csproj src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj

dotnet add tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj reference src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj
```

- [ ] **Step 4: Create `docker-compose.yml` at the repo root**

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

- [ ] **Step 5: Create `.gitignore` at the repo root**

```gitignore
## .NET
bin/
obj/
*.user
*.suo
.vs/

## Test results
[Tt]est[Rr]esult*/
*.trx

## Rider / VS Code
.idea/
*.DotSettings.user
```

- [ ] **Step 6: Verify the solution builds**

Run: `dotnet build`
Expected: `Build succeeded.` with 0 errors (warnings about unused usings from templates are fine).

- [ ] **Step 7: Commit**

```bash
git add RedeStore.sln src/ tests/ docker-compose.yml .gitignore
git commit -m "Scaffold RedeStore backend solution (4 layers + tests)"
```

---

## Task 2: Password Hashing

**Files:**
- Modify: `src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj` (add `FrameworkReference`)
- Create: `src/RedeStore.Application/Common/IPasswordHasher.cs`
- Create: `src/RedeStore.Infrastructure/Auth/PasswordHasher.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Auth/PasswordHasherTests.cs`

**Interfaces:**
- Produces: `RedeStore.Application.Common.IPasswordHasher` with `string HashPassword(string password)` and `bool VerifyPassword(string hash, string password)`. `RedeStore.Infrastructure.Auth.PasswordHasher` is the concrete implementation, registered as `IPasswordHasher` singleton in DI.

- [ ] **Step 1: Give Infrastructure access to ASP.NET Core's Identity hashing APIs**

Open `src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj` and add this `ItemGroup` (Infrastructure is a plain class library, so it needs an explicit framework reference to reach `Microsoft.AspNetCore.Identity`):

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing test**

Create `tests/RedeStore.UnitTests/Auth/PasswordHasherTests.cs`:

```csharp
using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class PasswordHasherTests
{
    private readonly PasswordHasher _sut = new();

    [Fact]
    public void HashPassword_ThenVerifyPassword_WithCorrectPassword_ReturnsTrue()
    {
        var hash = _sut.HashPassword("senha12345");

        var result = _sut.VerifyPassword(hash, "senha12345");

        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_ReturnsFalse()
    {
        var hash = _sut.HashPassword("senha12345");

        var result = _sut.VerifyPassword(hash, "senhaErrada");

        Assert.False(result);
    }

    [Fact]
    public void HashPassword_CalledTwiceWithSamePassword_ProducesDifferentHashes()
    {
        var hash1 = _sut.HashPassword("senha12345");
        var hash2 = _sut.HashPassword("senha12345");

        Assert.NotEqual(hash1, hash2);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~PasswordHasherTests`
Expected: FAIL to compile — `RedeStore.Infrastructure.Auth.PasswordHasher` does not exist yet.

- [ ] **Step 4: Implement `IPasswordHasher` and `PasswordHasher`**

Create `src/RedeStore.Application/Common/IPasswordHasher.cs`:

```csharp
namespace RedeStore.Application.Common;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string hash, string password);
}
```

Create `src/RedeStore.Infrastructure/Auth/PasswordHasher.cs`:

```csharp
using Microsoft.AspNetCore.Identity;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Auth;

public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly object HasherUser = new();
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<object> _hasher = new();

    public string HashPassword(string password) =>
        _hasher.HashPassword(HasherUser, password);

    public bool VerifyPassword(string hash, string password) =>
        _hasher.VerifyHashedPassword(HasherUser, hash, password)
            != PasswordVerificationResult.Failed;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~PasswordHasherTests`
Expected: 3 passed.

- [ ] **Step 6: Register the service in DI**

Open `src/RedeStore.Api/Program.cs` and replace its contents with:

```csharp
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

var app = builder.Build();

app.MapGet("/", () => "RedeStore API");

app.Run();
```

- [ ] **Step 7: Verify the API still builds and runs**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj src/RedeStore.Application/Common/IPasswordHasher.cs src/RedeStore.Infrastructure/Auth/PasswordHasher.cs src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Auth/PasswordHasherTests.cs
git commit -m "Add password hashing via ASP.NET Core Identity's PasswordHasher"
```

---

## Task 3: JWT Token Generation

**Files:**
- Modify: `src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj` (add `System.IdentityModel.Tokens.Jwt` package)
- Create: `src/RedeStore.Application/Common/IJwtTokenGenerator.cs`
- Create: `src/RedeStore.Infrastructure/Auth/JwtOptions.cs`
- Create: `src/RedeStore.Infrastructure/Auth/JwtTokenValidationParametersFactory.cs`
- Create: `src/RedeStore.Infrastructure/Auth/JwtTokenGenerator.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Auth/JwtTokenGeneratorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `RedeStore.Application.Common.IJwtTokenGenerator.GenerateToken(Guid usuarioId, string email, string papel) : string`. `RedeStore.Infrastructure.Auth.JwtOptions` (properties `SigningKey`, `Issuer`, `Audience`, `ExpirationHours`, bound from config section `"Jwt"`). `RedeStore.Infrastructure.Auth.JwtTokenValidationParametersFactory.Create(JwtOptions options) : TokenValidationParameters` — Task 6 reuses this exact factory so token generation and validation never drift apart.

- [ ] **Step 1: Add the JWT package to Infrastructure**

```bash
dotnet add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj package System.IdentityModel.Tokens.Jwt
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RedeStore.UnitTests/Auth/JwtTokenGeneratorTests.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "chave-de-teste-com-pelo-menos-32-caracteres",
        Issuer = "RedeStore.Tests",
        Audience = "RedeStore.Tests.Clients",
        ExpirationHours = 8,
    };

    private readonly JwtTokenGenerator _sut = new(Microsoft.Extensions.Options.Options.Create(Options));

    [Fact]
    public void GenerateToken_ProducesTokenWithExpectedClaims()
    {
        var usuarioId = Guid.NewGuid();

        var token = _sut.GenerateToken(usuarioId, "jovem@rede.com", "jovem");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(usuarioId.ToString(), jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("jovem@rede.com", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal("jovem", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
    }

    [Fact]
    public void GenerateToken_ProducesTokenThatPassesValidationWithSameOptions()
    {
        var usuarioId = Guid.NewGuid();
        var token = _sut.GenerateToken(usuarioId, "admin@rede.com", "admin");
        var validationParameters = JwtTokenValidationParametersFactory.Create(Options);

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);

        Assert.Equal("admin@rede.com", principal.FindFirst(JwtRegisteredClaimNames.Email)!.Value);
        Assert.Equal("admin", principal.FindFirst(ClaimTypes.Role)!.Value);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~JwtTokenGeneratorTests`
Expected: FAIL to compile — `JwtOptions`, `JwtTokenGenerator`, `JwtTokenValidationParametersFactory` don't exist yet.

- [ ] **Step 4: Implement the JWT primitives**

Create `src/RedeStore.Application/Common/IJwtTokenGenerator.cs`:

```csharp
namespace RedeStore.Application.Common;

public interface IJwtTokenGenerator
{
    string GenerateToken(Guid usuarioId, string email, string papel);
}
```

Create `src/RedeStore.Infrastructure/Auth/JwtOptions.cs`:

```csharp
namespace RedeStore.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string SigningKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int ExpirationHours { get; init; } = 8;
}
```

Create `src/RedeStore.Infrastructure/Auth/JwtTokenValidationParametersFactory.cs`:

```csharp
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RedeStore.Infrastructure.Auth;

public static class JwtTokenValidationParametersFactory
{
    public static TokenValidationParameters Create(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}
```

Create `src/RedeStore.Infrastructure/Auth/JwtTokenGenerator.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Auth;

public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(Guid usuarioId, string email, string papel)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuarioId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, papel),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_options.ExpirationHours),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~JwtTokenGeneratorTests`
Expected: 2 passed.

- [ ] **Step 6: Register the service in DI**

Open `src/RedeStore.Api/Program.cs` and replace its contents with:

```csharp
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

var app = builder.Build();

app.MapGet("/", () => "RedeStore API");

app.Run();
```

- [ ] **Step 7: Verify the whole suite still builds**

Run: `dotnet build`
Expected: `Build succeeded.` (the app will not run correctly yet without a configured `Jwt` section — that's expected, it's wired for real in Task 6).

- [ ] **Step 8: Commit**

```bash
git add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj src/RedeStore.Application/Common/IJwtTokenGenerator.cs src/RedeStore.Infrastructure/Auth/JwtOptions.cs src/RedeStore.Infrastructure/Auth/JwtTokenValidationParametersFactory.cs src/RedeStore.Infrastructure/Auth/JwtTokenGenerator.cs src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Auth/JwtTokenGeneratorTests.cs
git commit -m "Add JWT token generation and validation parameters factory"
```

---

## Task 4: PostgreSQL Connectivity

**Files:**
- Modify: `src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj` (add EF Core + Npgsql packages)
- Modify: `src/RedeStore.Api/RedeStore.Api.csproj` (add `Microsoft.EntityFrameworkCore.Design`)
- Modify: `tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj` (add `Testcontainers.PostgreSql`)
- Create: `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.IntegrationTests/Persistence/RedeStoreDbContextTests.cs`

**Interfaces:**
- Produces: `RedeStore.Infrastructure.Persistence.RedeStoreDbContext` (empty `DbContext`, zero `DbSet<T>` — Fase 2 adds the first entities). Registered in DI reading `ConnectionStrings:Default` from configuration.

- [ ] **Step 1: Install the EF Core CLI tool globally (one-time machine setup)**

```bash
dotnet tool install --global dotnet-ef
```

Run: `dotnet ef --version`
Expected: prints a version number (confirms the tool is on PATH).

- [ ] **Step 2: Add the required packages**

```bash
dotnet add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj package Microsoft.EntityFrameworkCore
dotnet add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/RedeStore.Api/RedeStore.Api.csproj package Microsoft.EntityFrameworkCore.Design
dotnet add tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj package Testcontainers.PostgreSql
```

- [ ] **Step 3: Write the failing integration test**

Create `tests/RedeStore.IntegrationTests/Persistence/RedeStoreDbContextTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

public class RedeStoreDbContextTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task CanConnectAsync_WithRealPostgresContainer_ReturnsTrue()
    {
        var options = new DbContextOptionsBuilder<RedeStoreDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var dbContext = new RedeStoreDbContext(options);

        var canConnect = await dbContext.Database.CanConnectAsync();

        Assert.True(canConnect);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~RedeStoreDbContextTests`
Expected: FAIL to compile — `RedeStoreDbContext` doesn't exist yet.

- [ ] **Step 5: Implement `RedeStoreDbContext`**

Create `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace RedeStore.Infrastructure.Persistence;

public sealed class RedeStoreDbContext : DbContext
{
    public RedeStoreDbContext(DbContextOptions<RedeStoreDbContext> options) : base(options)
    {
    }
}
```

- [ ] **Step 6: Run the test to verify it passes (requires Docker running)**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~RedeStoreDbContextTests`
Expected: 1 passed. (Testcontainers pulls `postgres:17` on first run — may take a minute.)

- [ ] **Step 7: Start local Postgres and configure the connection string via user-secrets**

```bash
docker compose up -d db
dotnet user-secrets init --project src/RedeStore.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=redestore;Username=postgres;Password=postgres" --project src/RedeStore.Api
```

- [ ] **Step 8: Register `RedeStoreDbContext` in DI**

Open `src/RedeStore.Api/Program.cs` and replace its contents with:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

var app = builder.Build();

app.MapGet("/", () => "RedeStore API");

app.Run();
```

- [ ] **Step 9: Verify the build still succeeds**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 10: Commit**

```bash
git add src/RedeStore.Infrastructure/RedeStore.Infrastructure.csproj src/RedeStore.Api/RedeStore.Api.csproj tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Persistence/RedeStoreDbContextTests.cs
git commit -m "Add PostgreSQL connectivity via empty RedeStoreDbContext"
```

*(Note: `dotnet user-secrets` writes to a per-user file outside the repo, not to a trackable file — nothing to add there.)*

---

## Task 5: Domain Exceptions & Global Error Handling

**Files:**
- Modify: `tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj` (add reference to `RedeStore.Api` + `FrameworkReference`)
- Create: `src/RedeStore.Domain/Exceptions/DomainException.cs`
- Create: `src/RedeStore.Api/Middleware/GlobalExceptionHandler.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Middleware/GlobalExceptionHandlerTests.cs`

**Interfaces:**
- Produces: `RedeStore.Domain.Exceptions.DomainException` (abstract; `abstract string Codigo { get; }`, `abstract int StatusCode { get; }`) — every future business-rule exception (Fase 2+, e.g. `EmailEmUsoException`) inherits from this. `RedeStore.Api.Middleware.GlobalExceptionHandler` implements `Microsoft.AspNetCore.Diagnostics.IExceptionHandler`, mapping any `DomainException` to its own status/code and everything else to a generic `500`/`"ERRO_INTERNO"` `ProblemDetails` (never leaking internal exception messages).

- [ ] **Step 1: Let UnitTests reference the Api project and ASP.NET Core types**

```bash
dotnet add tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj reference src/RedeStore.Api/RedeStore.Api.csproj
```

Open `tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj` and add this `ItemGroup` (needed for `DefaultHttpContext` in the test below):

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RedeStore.UnitTests/Middleware/GlobalExceptionHandlerTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RedeStore.Api.Middleware;
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Middleware;

public class GlobalExceptionHandlerTests
{
    private sealed class ExemploDomainException() : DomainException("mensagem de teste")
    {
        public override string Codigo => "ERRO_EXEMPLO";
        public override int StatusCode => StatusCodes.Status409Conflict;
    }

    private readonly GlobalExceptionHandler _sut = new();

    [Fact]
    public async Task TryHandleAsync_WithDomainException_WritesMappedProblemDetails()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await _sut.TryHandleAsync(httpContext, new ExemploDomainException(), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal("ERRO_EXEMPLO", problemDetails!.Title);
        Assert.Equal("mensagem de teste", problemDetails.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_WithUnmappedException_Returns500WithGenericMessage()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handled = await _sut.TryHandleAsync(httpContext, new InvalidOperationException("detalhe interno sensivel"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(httpContext.Response.Body);
        Assert.Equal("ERRO_INTERNO", problemDetails!.Title);
        Assert.DoesNotContain("detalhe interno sensivel", problemDetails.Detail);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~GlobalExceptionHandlerTests`
Expected: FAIL to compile — `DomainException` and `GlobalExceptionHandler` don't exist yet.

- [ ] **Step 4: Implement `DomainException` and `GlobalExceptionHandler`**

Create `src/RedeStore.Domain/Exceptions/DomainException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public abstract class DomainException : Exception
{
    public abstract string Codigo { get; }
    public abstract int StatusCode { get; }

    protected DomainException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Api/Middleware/GlobalExceptionHandler.cs`:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Api.Middleware;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, codigo, mensagem) = exception switch
        {
            DomainException domainException => (domainException.StatusCode, domainException.Codigo, domainException.Message),
            _ => (StatusCodes.Status500InternalServerError, "ERRO_INTERNO", "Ocorreu um erro inesperado."),
        };

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = codigo,
            Detail = mensagem,
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~GlobalExceptionHandlerTests`
Expected: 2 passed.

- [ ] **Step 6: Register the handler in the pipeline**

Open `src/RedeStore.Api/Program.cs` and replace its contents with:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Api.Middleware;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => "RedeStore API");

app.Run();
```

- [ ] **Step 7: Verify the build still succeeds**

Run: `dotnet build`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add tests/RedeStore.UnitTests/RedeStore.UnitTests.csproj src/RedeStore.Domain/Exceptions/DomainException.cs src/RedeStore.Api/Middleware/GlobalExceptionHandler.cs src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Middleware/GlobalExceptionHandlerTests.cs
git commit -m "Add global exception handling mapped to ProblemDetails"
```

---

## Task 6: JWT Authentication Wiring

**Files:**
- Modify: `src/RedeStore.Api/RedeStore.Api.csproj` (add `Microsoft.AspNetCore.Authentication.JwtBearer`)
- Modify: `src/RedeStore.Api/Program.cs`

**Interfaces:**
- Consumes: `RedeStore.Infrastructure.Auth.JwtOptions` and `JwtTokenValidationParametersFactory.Create` (Task 3) — this task is what actually plugs them into the ASP.NET Core authentication pipeline.
- Produces: an `"Admin"` authorization policy (`RequireRole("admin")`) that Fase 3+ endpoints will use via `.RequireAuthorization("Admin")`.

- [ ] **Step 1: Add the JWT Bearer package**

```bash
dotnet add src/RedeStore.Api/RedeStore.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Configure JWT signing secrets via user-secrets**

```bash
dotnet user-secrets set "Jwt:SigningKey" "troque-esta-chave-por-uma-gerada-com-pelo-menos-32-caracteres" --project src/RedeStore.Api
dotnet user-secrets set "Jwt:Issuer" "RedeStore.Api" --project src/RedeStore.Api
dotnet user-secrets set "Jwt:Audience" "RedeStore.Clients" --project src/RedeStore.Api
```

- [ ] **Step 3: Wire authentication and authorization into `Program.cs`**

Open `src/RedeStore.Api/Program.cs` and replace its contents with:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using RedeStore.Api.Middleware;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuração 'Jwt' ausente. Configure via dotnet user-secrets.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = JwtTokenValidationParametersFactory.Create(jwtOptions);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "RedeStore API");

app.Run();
```

- [ ] **Step 4: Verify the build succeeds and the existing suite is still green**

Run: `dotnet build`
Expected: `Build succeeded.`

Run: `dotnet test`
Expected: all previously-written tests (PasswordHasher, JwtTokenGenerator, GlobalExceptionHandler, RedeStoreDbContext) still pass. This task adds no new tests of its own — the JWT validation logic it wires up was already proven end-to-end by `JwtTokenGeneratorTests.GenerateToken_ProducesTokenThatPassesValidationWithSameOptions` in Task 3.

- [ ] **Step 5: Commit**

```bash
git add src/RedeStore.Api/RedeStore.Api.csproj src/RedeStore.Api/Program.cs
git commit -m "Wire JWT bearer authentication and Admin authorization policy"
```

---

## Task 7: OpenAPI Docs, CORS, Health Check & Final Smoke Test

**Files:**
- Modify: `src/RedeStore.Api/RedeStore.Api.csproj` (add `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore`)
- Modify: `src/RedeStore.Api/appsettings.Development.json`
- Modify: `src/RedeStore.Api/Program.cs`

**Interfaces:**
- Produces: `GET /health` (public, checks DB connectivity), `GET /scalar/v1` (dev-only API docs UI), a named CORS policy allowing the Angular dev server. `public partial class Program;` marker at the bottom of `Program.cs` so Fase 2's `WebApplicationFactory<Program>`-based integration tests can reference the entry point.

- [ ] **Step 1: Add the remaining packages**

```bash
dotnet add src/RedeStore.Api/RedeStore.Api.csproj package Microsoft.AspNetCore.OpenApi
dotnet add src/RedeStore.Api/RedeStore.Api.csproj package Scalar.AspNetCore
```

- [ ] **Step 2: Configure the allowed CORS origin for dev**

Open `src/RedeStore.Api/appsettings.Development.json` and set its contents to:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Cors": {
    "AllowedOrigin": "http://localhost:4200"
  }
}
```

- [ ] **Step 3: Assemble the final `Program.cs`**

Open `src/RedeStore.Api/Program.cs` and replace its contents with:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using RedeStore.Api.Middleware;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Auth;
using RedeStore.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

const string FrontendCorsPolicy = "FrontendCorsPolicy";

builder.Services.AddDbContext<RedeStoreDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuração 'Jwt' ausente. Configure via dotnet user-secrets.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = JwtTokenValidationParametersFactory.Create(jwtOptions);
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("admin"));
});

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health", async (RedeStoreDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { status = "healthy", database = "connected" })
        : Results.Problem("Não foi possível conectar ao banco de dados.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();

public partial class Program;
```

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: all tests across `RedeStore.UnitTests` and `RedeStore.IntegrationTests` pass (Docker must be running for the Testcontainers-based test).

- [ ] **Step 5: Manual smoke test — run the API and hit it for real**

```bash
docker compose up -d db
dotnet run --project src/RedeStore.Api
```

Note the URL printed in the console (e.g. `Now listening on: http://localhost:5231`). In another terminal:

```bash
curl http://localhost:5231/health
```

Expected: `{"status":"healthy","database":"connected"}` with HTTP 200.

```bash
curl -I http://localhost:5231/scalar/v1
```

Expected: HTTP 200 (the Scalar docs UI is reachable).

Stop the API with `Ctrl+C` when done.

- [ ] **Step 6: Commit**

```bash
git add src/RedeStore.Api/RedeStore.Api.csproj src/RedeStore.Api/appsettings.Development.json src/RedeStore.Api/Program.cs
git commit -m "Add OpenAPI/Scalar docs, CORS for Angular dev server, and /health endpoint"
```

---

## Definition of Done (Fase 1)

- `dotnet build` and `dotnet test` succeed from the repo root with no manual fixes.
- `docker compose up -d db` + `dotnet run --project src/RedeStore.Api` boots the API with zero business endpoints.
- `GET /health` reports the database as connected.
- `GET /scalar/v1` shows the (currently empty) API documentation.
- A JWT generated by `IJwtTokenGenerator` is accepted by the API's own authentication scheme (proven by test, not yet by a live protected endpoint — the first protected endpoints arrive in Fase 2).
- No `Usuario`/`Produto`/`Evento`/`Pedido` entities exist yet — that's correct, they belong to Fases 2-5 per `docs/2026-08-26-rede-backend-design.md` section 9.
