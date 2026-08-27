# Backend REDE — Fase 2 (Auth & Usuários) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the first real business subsystem — user registration, login, profile, and password reset — on top of the Fase 1 foundation, with a `WebApplicationFactory` + Testcontainers integration harness proving the whole flow end-to-end against a real Postgres.

**Architecture:** New `Usuario`/`PasswordResetToken` entities and their EF Core migration; repository interfaces in `Application.Common` (implementations in `Infrastructure.Persistence`, required by the fixed dependency graph); a single `AuthService` covering all auth use cases; JWT/password-hashing reused unchanged from Fase 1; a generic `ValidationFilter<T>` wiring FluentValidation into Minimal API endpoints; password-reset e-mail sent via Resend's HTTP API directly (no SDK).

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (already wired), FluentValidation, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`), Testcontainers.PostgreSql (already used in Fase 1), Resend HTTP API via `HttpClient`.

**Spec:** `docs/2026-08-27-rede-backend-fase2-design.md` (this plan implements it end to end) and `docs/2026-08-26-rede-backend-design.md` (general architecture) / `docs/2026-08-26-rede-backend-requisitos.md` (domain rules).

## Global Constraints

- Target framework `net10.0` for every project (already set).
- Fixed dependency graph from Fase 1: `Domain` → nothing; `Application` → `Domain` only; `Infrastructure` → `Application` + `Domain`; `Api` → `Application` + `Infrastructure`. Repository **interfaces** live in `RedeStore.Application.Common`; **implementations** live in `RedeStore.Infrastructure.Persistence.Repositories`. `Application` must never reference `Infrastructure` or EF Core types directly.
- Password reset tokens are hashed with SHA-256 (`System.Security.Cryptography`), **never** with `IPasswordHasher` — the token already has 32 bytes of entropy from `RandomNumberGenerator`, so the deliberately-slow password hasher would only add latency with no security benefit. `IPasswordHasher` (Fase 1) is used only for actual user passwords.
- `Papel` enum values (`Jovem`, `Admin`) map 1:1 to the lowercase wire strings `"jovem"`/`"admin"` via `papel.ToString().ToLowerInvariant()` — this exact mapping is used everywhere a `Papel` crosses into a DTO or a JWT claim, so it must stay consistent.
- No mocking library. Unit tests needing a fake repository use a small hand-rolled in-memory class implementing the same interface — not Moq/NSubstitute.
- Every new `DomainException` subclass (`EmailEmUsoException`, `CredenciaisInvalidasException`, `TokenInvalidoException`, `AcessoNegadoException`) only overrides `Codigo`/`StatusCode` and takes a `message` — no extra logic, matching the Fase 1 `DomainException` base and the already-existing `GlobalExceptionHandler`, which needs no changes in this plan (it already handles any `DomainException` polymorphically).
- Integration tests must never call the real Resend API — the `WebApplicationFactory`-based harness always substitutes `IEmailSender` with an in-memory fake.
- Secrets (Resend API key) via `dotnet user-secrets`, never committed to any `appsettings*.json`.
- Endpoints are Minimal API, grouped via `MapGroup`, following the `Api/Endpoints/` convention already established in the design doc.
- Shared testing convention: integration tests that create a `Usuario` must use a unique, randomly-suffixed e-mail per test (e.g. `$"{Guid.NewGuid()}@teste.com"`) — the Postgres container and its data are shared across the whole integration test run via `ICollectionFixture`, so tests must not collide on the unique e-mail index.

---

## Task 1: WebApplicationFactory + Testcontainers Integration Harness

**Files:**
- Modify: `tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj`
- Create: `tests/RedeStore.IntegrationTests/ApiFactory.cs`
- Create: `tests/RedeStore.IntegrationTests/IntegrationTestCollection.cs`
- Create: `tests/RedeStore.IntegrationTests/HealthEndpointTests.cs`

**Interfaces:**
- Produces: `RedeStore.IntegrationTests.ApiFactory` — a `WebApplicationFactory<Program>` that starts a real `postgres:17` Testcontainer, overrides `ConnectionStrings:Default` and the `Jwt:*` settings via in-memory configuration, and runs `Database.MigrateAsync()` before any test uses it. `RedeStore.IntegrationTests.IntegrationTestCollection` (xUnit collection name `"Integration"`) — every later integration test class in this plan uses `[Collection(IntegrationTestCollection.Name)]` and takes `ApiFactory` via constructor injection, sharing one container across the whole run.

- [ ] **Step 1: Add the required package and project reference**

```bash
dotnet add tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj reference src/RedeStore.Api/RedeStore.Api.csproj
```

- [ ] **Step 2: Write the failing test**

Create `tests/RedeStore.IntegrationTests/HealthEndpointTests.cs`:

```csharp
using System.Net.Http.Json;
using Xunit;

namespace RedeStore.IntegrationTests;

[Collection(IntegrationTestCollection.Name)]
public class HealthEndpointTests
{
    private readonly HttpClient _client;

    public HealthEndpointTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private sealed record HealthResponse(string Status, string Database);

    [Fact]
    public async Task GetHealth_ReturnsHealthyWithDatabaseConnected()
    {
        var response = await _client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("healthy", body!.Status);
        Assert.Equal("connected", body.Database);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~HealthEndpointTests`
Expected: FAIL to compile — `ApiFactory` and `IntegrationTestCollection` don't exist yet.

- [ ] **Step 4: Implement `ApiFactory` and `IntegrationTestCollection`**

Create `tests/RedeStore.IntegrationTests/ApiFactory.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RedeStore.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:SigningKey"] = "chave-de-teste-para-integracao-com-pelo-menos-32-caracteres",
                ["Jwt:Issuer"] = "RedeStore.IntegrationTests",
                ["Jwt:Audience"] = "RedeStore.IntegrationTests.Clients",
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RedeStoreDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

Create `tests/RedeStore.IntegrationTests/IntegrationTestCollection.cs`:

```csharp
using Xunit;

namespace RedeStore.IntegrationTests;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Integration";
}
```

- [ ] **Step 5: Run the test to verify it passes (requires Docker running)**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~HealthEndpointTests`
Expected: 1 passed. (First run pulls `postgres:17` if not cached — may take a minute.)

- [ ] **Step 6: Run the full suite to confirm nothing else regressed**

Run: `dotnet test`
Expected: all Fase 1 tests still pass, plus this new one.

- [ ] **Step 7: Commit**

```bash
git add tests/RedeStore.IntegrationTests/RedeStore.IntegrationTests.csproj tests/RedeStore.IntegrationTests/ApiFactory.cs tests/RedeStore.IntegrationTests/IntegrationTestCollection.cs tests/RedeStore.IntegrationTests/HealthEndpointTests.cs
git commit -m "Add WebApplicationFactory + Testcontainers integration harness"
```

---

## Task 2: Usuario & PasswordResetToken Persistence

**Files:**
- Create: `src/RedeStore.Domain/Entities/Papel.cs`
- Create: `src/RedeStore.Domain/Entities/Usuario.cs`
- Create: `src/RedeStore.Domain/Entities/PasswordResetToken.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/UsuarioConfiguration.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/PasswordResetTokenConfiguration.cs`
- Modify: `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs`
- Create: `src/RedeStore.Application/Common/IUsuarioRepository.cs`
- Create: `src/RedeStore.Application/Common/IPasswordResetTokenRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/UsuarioRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/PasswordResetTokenRepository.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Migration: generated under `src/RedeStore.Infrastructure/Persistence/Migrations/`
- Test: `tests/RedeStore.IntegrationTests/Persistence/UsuarioRepositoryTests.cs`
- Test: `tests/RedeStore.IntegrationTests/Persistence/PasswordResetTokenRepositoryTests.cs`

**Interfaces:**
- Consumes: `ApiFactory`/`IntegrationTestCollection` (Task 1).
- Produces: `RedeStore.Application.Common.IUsuarioRepository` — `Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct)`, `Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct)`, `Task AdicionarAsync(Usuario usuario, CancellationToken ct)`, `Task AtualizarAsync(Usuario usuario, CancellationToken ct)`. `RedeStore.Application.Common.IPasswordResetTokenRepository` — `Task AdicionarAsync(PasswordResetToken token, CancellationToken ct)`, `Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct)`, `Task AtualizarAsync(PasswordResetToken token, CancellationToken ct)`. Both registered as `Scoped` in DI. Task 5 and Task 6 depend on these exact signatures.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Persistence/UsuarioRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class UsuarioRepositoryTests
{
    private readonly ApiFactory _factory;

    public UsuarioRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorEmailAsync_RetornaOMesmoUsuario()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Fulano de Tal",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };

        await repositorio.AdicionarAsync(usuario, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorEmailAsync(usuario.Email, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(usuario.Nome, encontrado!.Nome);
        Assert.Equal(Papel.Jovem, encontrado.Papel);
    }

    [Fact]
    public async Task BuscarPorEmailAsync_ComEmailInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();

        var encontrado = await repositorio.BuscarPorEmailAsync($"{Guid.NewGuid()}@naoexiste.com", CancellationToken.None);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task AtualizarAsync_PersisteMudancaDeNome()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Nome Original",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };
        await repositorio.AdicionarAsync(usuario, CancellationToken.None);

        usuario.Nome = "Nome Atualizado";
        await repositorio.AtualizarAsync(usuario, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(usuario.Id, CancellationToken.None);

        Assert.Equal("Nome Atualizado", encontrado!.Nome);
    }
}
```

Create `tests/RedeStore.IntegrationTests/Persistence/PasswordResetTokenRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class PasswordResetTokenRepositoryTests
{
    private readonly ApiFactory _factory;

    public PasswordResetTokenRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<Usuario> CriarUsuarioAsync(IUsuarioRepository usuarioRepositorio)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Fulano",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };
        await usuarioRepositorio.AdicionarAsync(usuario, CancellationToken.None);
        return usuario;
    }

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorTokenHashAsync_RetornaOMesmoToken()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var tokenRepositorio = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>();
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiraEm = DateTime.UtcNow.AddHours(1),
        };

        await tokenRepositorio.AdicionarAsync(token, CancellationToken.None);
        var encontrado = await tokenRepositorio.BuscarPorTokenHashAsync(token.TokenHash, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(usuario.Id, encontrado!.UsuarioId);
        Assert.Null(encontrado.UsadoEm);
    }

    [Fact]
    public async Task AtualizarAsync_MarcaTokenComoUsado()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var tokenRepositorio = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>();
        var usuario = await CriarUsuarioAsync(usuarioRepositorio);
        var token = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            ExpiraEm = DateTime.UtcNow.AddHours(1),
        };
        await tokenRepositorio.AdicionarAsync(token, CancellationToken.None);

        token.UsadoEm = DateTime.UtcNow;
        await tokenRepositorio.AtualizarAsync(token, CancellationToken.None);
        var encontrado = await tokenRepositorio.BuscarPorTokenHashAsync(token.TokenHash, CancellationToken.None);

        Assert.NotNull(encontrado!.UsadoEm);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~UsuarioRepositoryTests|FullyQualifiedName~PasswordResetTokenRepositoryTests"`
Expected: FAIL to compile — none of the entities/interfaces/repositories exist yet.

- [ ] **Step 3: Create the entities**

Create `src/RedeStore.Domain/Entities/Papel.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public enum Papel
{
    Jovem,
    Admin,
}
```

Create `src/RedeStore.Domain/Entities/Usuario.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Usuario
{
    public Guid Id { get; set; }
    public required string Nome { get; set; }
    public required string Email { get; set; }
    public string? Telefone { get; set; }
    public Papel Papel { get; set; }
    public required string SenhaHash { get; set; }
}
```

Create `src/RedeStore.Domain/Entities/PasswordResetToken.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
}
```

- [ ] **Step 4: Configure the entities and wire them into `RedeStoreDbContext`**

Create `src/RedeStore.Infrastructure/Persistence/Configurations/UsuarioConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class UsuarioConfiguration : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("Usuarios");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Nome).IsRequired().HasMaxLength(200);
        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.Telefone).HasMaxLength(20);
        builder.Property(u => u.Papel).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.SenhaHash).IsRequired();
    }
}
```

Create `src/RedeStore.Infrastructure/Persistence/Configurations/PasswordResetTokenConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasOne<Usuario>().WithMany().HasForeignKey(t => t.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

Open `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs` and replace its contents with:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence;

public sealed class RedeStoreDbContext : DbContext
{
    public RedeStoreDbContext(DbContextOptions<RedeStoreDbContext> options) : base(options)
    {
    }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RedeStoreDbContext).Assembly);
    }
}
```

- [ ] **Step 5: Create the repository interfaces and implementations**

Create `src/RedeStore.Application/Common/IUsuarioRepository.cs`:

```csharp
using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IUsuarioRepository
{
    Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct);
    Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Usuario usuario, CancellationToken ct);
    Task AtualizarAsync(Usuario usuario, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Common/IPasswordResetTokenRepository.cs`:

```csharp
using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IPasswordResetTokenRepository
{
    Task AdicionarAsync(PasswordResetToken token, CancellationToken ct);
    Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct);
    Task AtualizarAsync(PasswordResetToken token, CancellationToken ct);
}
```

Create `src/RedeStore.Infrastructure/Persistence/Repositories/UsuarioRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class UsuarioRepository : IUsuarioRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public UsuarioRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct) =>
        _dbContext.Usuarios.SingleOrDefaultAsync(u => u.Email == email, ct);

    public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);

    public async Task AdicionarAsync(Usuario usuario, CancellationToken ct)
    {
        _dbContext.Usuarios.Add(usuario);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Usuario usuario, CancellationToken ct)
    {
        _dbContext.Usuarios.Update(usuario);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

Create `src/RedeStore.Infrastructure/Persistence/Repositories/PasswordResetTokenRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public PasswordResetTokenRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AdicionarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _dbContext.PasswordResetTokens.Add(token);
        await _dbContext.SaveChangesAsync(ct);
    }

    public Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct) =>
        _dbContext.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AtualizarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _dbContext.PasswordResetTokens.Update(token);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Register the repositories in DI**

Open `src/RedeStore.Api/Program.cs` and add these two lines right after the `AddDbContext<RedeStoreDbContext>` registration (keep everything else in the file unchanged):

```csharp
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
```

Add these two `using` statements at the top:

```csharp
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Persistence.Repositories;
```

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add InitialAuth --project src/RedeStore.Infrastructure --startup-project src/RedeStore.Api --output-dir Persistence/Migrations
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~UsuarioRepositoryTests|FullyQualifiedName~PasswordResetTokenRepositoryTests"`
Expected: 5 passed. (`ApiFactory.InitializeAsync` applies the new `InitialAuth` migration automatically.)

- [ ] **Step 9: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 10: Commit**

```bash
git add src/RedeStore.Domain/Entities/ src/RedeStore.Infrastructure/Persistence/Configurations/ src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs src/RedeStore.Application/Common/IUsuarioRepository.cs src/RedeStore.Application/Common/IPasswordResetTokenRepository.cs src/RedeStore.Infrastructure/Persistence/Repositories/ src/RedeStore.Infrastructure/Persistence/Migrations/ src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Persistence/
git commit -m "Add Usuario and PasswordResetToken persistence with first EF Core migration"
```

---

## Task 3: FluentValidation + Generic `ValidationFilter<T>`

**Files:**
- Modify: `src/RedeStore.Api/RedeStore.Api.csproj`
- Create: `src/RedeStore.Api/Filters/ValidationFilter.cs`
- Test: `tests/RedeStore.UnitTests/Filters/ValidationFilterTests.cs`

**Interfaces:**
- Produces: `RedeStore.Api.Filters.ValidationFilter<T>` implementing `IEndpointFilter`, constructor `ValidationFilter(IValidator<T> validator)`. Registered per-endpoint via `.AddEndpointFilter<ValidationFilter<TRequest>>()` — Task 7 relies on this exact usage pattern, with each `IValidator<TRequest>` registered as a DI singleton (validators are stateless).

- [ ] **Step 1: Add the FluentValidation package**

```bash
dotnet add src/RedeStore.Api/RedeStore.Api.csproj package FluentValidation
```

- [ ] **Step 2: Write the failing tests**

Create `tests/RedeStore.UnitTests/Filters/ValidationFilterTests.cs`:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Http;
using RedeStore.Api.Filters;
using Xunit;

namespace RedeStore.UnitTests.Filters;

public class ValidationFilterTests
{
    private sealed record TesteRequest(string Nome);

    private sealed class TesteRequestValidator : AbstractValidator<TesteRequest>
    {
        public TesteRequestValidator()
        {
            RuleFor(r => r.Nome).MinimumLength(2);
        }
    }

    [Fact]
    public async Task InvokeAsync_ComRequestInvalido_NaoChamaProximoDelegateERetornaValidationProblem()
    {
        var filter = new ValidationFilter<TesteRequest>(new TesteRequestValidator());
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), new TesteRequest("a"));
        var proximoChamado = false;

        var resultado = await filter.InvokeAsync(context, _ =>
        {
            proximoChamado = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.False(proximoChamado);
        Assert.IsAssignableFrom<IResult>(resultado);
    }

    [Fact]
    public async Task InvokeAsync_ComRequestValido_ChamaProximoDelegate()
    {
        var filter = new ValidationFilter<TesteRequest>(new TesteRequestValidator());
        var context = EndpointFilterInvocationContext.Create(new DefaultHttpContext(), new TesteRequest("Nome Valido"));
        var proximoChamado = false;

        await filter.InvokeAsync(context, _ =>
        {
            proximoChamado = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        });

        Assert.True(proximoChamado);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~ValidationFilterTests`
Expected: FAIL to compile — `ValidationFilter<T>` doesn't exist yet.

- [ ] **Step 4: Implement `ValidationFilter<T>`**

Create `src/RedeStore.Api/Filters/ValidationFilter.cs`:

```csharp
using FluentValidation;

namespace RedeStore.Api.Filters;

public sealed class ValidationFilter<T> : IEndpointFilter
{
    private readonly IValidator<T> _validator;

    public ValidationFilter(IValidator<T> validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argumento = context.Arguments.OfType<T>().First();
        var resultado = await _validator.ValidateAsync(argumento);

        if (!resultado.IsValid)
        {
            return Results.ValidationProblem(resultado.ToDictionary());
        }

        return await next(context);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~ValidationFilterTests`
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/RedeStore.Api/RedeStore.Api.csproj src/RedeStore.Api/Filters/ValidationFilter.cs tests/RedeStore.UnitTests/Filters/ValidationFilterTests.cs
git commit -m "Add generic FluentValidation endpoint filter"
```

---

## Task 4: Auth Domain Exceptions

**Files:**
- Create: `src/RedeStore.Domain/Exceptions/EmailEmUsoException.cs`
- Create: `src/RedeStore.Domain/Exceptions/CredenciaisInvalidasException.cs`
- Create: `src/RedeStore.Domain/Exceptions/TokenInvalidoException.cs`
- Create: `src/RedeStore.Domain/Exceptions/AcessoNegadoException.cs`
- Test: `tests/RedeStore.UnitTests/Domain/DomainExceptionsTests.cs`

**Interfaces:**
- Consumes: `RedeStore.Domain.Exceptions.DomainException` (Fase 1) — each subclass overrides `Codigo`/`StatusCode` and takes a `string message` constructor param, same shape as Fase 1's pattern.
- Produces: `EmailEmUsoException` (`"EMAIL_EM_USO"`, 409), `CredenciaisInvalidasException` (`"CREDENCIAIS_INVALIDAS"`, 401), `TokenInvalidoException` (`"TOKEN_INVALIDO"`, 400), `AcessoNegadoException` (`"ACESSO_NEGADO"`, 403) — Task 5 and Task 6 throw these exact types.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.UnitTests/Domain/DomainExceptionsTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class DomainExceptionsTests
{
    [Fact]
    public void EmailEmUsoException_TemCodigoEStatusCorretos()
    {
        var exception = new EmailEmUsoException("e-mail já cadastrado");
        Assert.Equal("EMAIL_EM_USO", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public void CredenciaisInvalidasException_TemCodigoEStatusCorretos()
    {
        var exception = new CredenciaisInvalidasException("credenciais inválidas");
        Assert.Equal("CREDENCIAIS_INVALIDAS", exception.Codigo);
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public void TokenInvalidoException_TemCodigoEStatusCorretos()
    {
        var exception = new TokenInvalidoException("token inválido");
        Assert.Equal("TOKEN_INVALIDO", exception.Codigo);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public void AcessoNegadoException_TemCodigoEStatusCorretos()
    {
        var exception = new AcessoNegadoException("acesso negado");
        Assert.Equal("ACESSO_NEGADO", exception.Codigo);
        Assert.Equal(403, exception.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~DomainExceptionsTests`
Expected: FAIL to compile — none of the 4 exception types exist yet.

- [ ] **Step 3: Implement the 4 exception classes**

Create `src/RedeStore.Domain/Exceptions/EmailEmUsoException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class EmailEmUsoException : DomainException
{
    public override string Codigo => "EMAIL_EM_USO";
    public override int StatusCode => 409;

    public EmailEmUsoException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/CredenciaisInvalidasException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class CredenciaisInvalidasException : DomainException
{
    public override string Codigo => "CREDENCIAIS_INVALIDAS";
    public override int StatusCode => 401;

    public CredenciaisInvalidasException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/TokenInvalidoException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class TokenInvalidoException : DomainException
{
    public override string Codigo => "TOKEN_INVALIDO";
    public override int StatusCode => 400;

    public TokenInvalidoException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/AcessoNegadoException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class AcessoNegadoException : DomainException
{
    public override string Codigo => "ACESSO_NEGADO";
    public override int StatusCode => 403;

    public AcessoNegadoException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~DomainExceptionsTests`
Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/RedeStore.Domain/Exceptions/EmailEmUsoException.cs src/RedeStore.Domain/Exceptions/CredenciaisInvalidasException.cs src/RedeStore.Domain/Exceptions/TokenInvalidoException.cs src/RedeStore.Domain/Exceptions/AcessoNegadoException.cs tests/RedeStore.UnitTests/Domain/DomainExceptionsTests.cs
git commit -m "Add auth-related domain exceptions"
```

---

## Task 5: AuthService — Cadastro, Login, Perfil, Autorização por Id

**Files:**
- Create: `src/RedeStore.Application/Auth/Dtos/UsuarioDto.cs`
- Create: `src/RedeStore.Application/Auth/Dtos/CadastroRequest.cs`
- Create: `src/RedeStore.Application/Auth/Dtos/LoginRequest.cs`
- Create: `src/RedeStore.Application/Auth/Dtos/AuthResponse.cs`
- Create: `src/RedeStore.Application/Auth/Dtos/AtualizarPerfilRequest.cs`
- Create: `src/RedeStore.Application/Auth/Validators/CadastroRequestValidator.cs`
- Create: `src/RedeStore.Application/Auth/Validators/LoginRequestValidator.cs`
- Create: `src/RedeStore.Application/Auth/Validators/AtualizarPerfilRequestValidator.cs`
- Create: `src/RedeStore.Application/Auth/IAuthService.cs`
- Create: `src/RedeStore.Application/Auth/AuthService.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Auth/FakeUsuarioRepository.cs`
- Test: `tests/RedeStore.UnitTests/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IUsuarioRepository` (Task 2), `EmailEmUsoException`/`CredenciaisInvalidasException`/`AcessoNegadoException` (Task 4), Fase 1's `IPasswordHasher` (`HashPassword`/`VerifyPassword`) and `IJwtTokenGenerator.GenerateToken(Guid usuarioId, string email, string papel)`.
- Produces: `RedeStore.Application.Auth.IAuthService` — `Task<AuthResponse> CadastrarAsync(CadastroRequest, CancellationToken)`, `Task<AuthResponse> LoginAsync(LoginRequest, CancellationToken)`, `Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken)`, `Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken)`, `Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest, CancellationToken)`. `UsuarioDto(Guid Id, string Nome, string Email, string? Telefone, string Papel)` — `Papel` is the lowercase string (`"jovem"`/`"admin"`), never the enum. Task 6 adds two more methods to this same service/interface (`RecuperarSenhaAsync`/`RedefinirSenhaAsync`); Task 7 maps all of these to HTTP endpoints.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.UnitTests/Auth/FakeUsuarioRepository.cs` (an in-memory test double, not a mock — Global Constraints forbid mocking libraries):

```csharp
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Auth;

public sealed class FakeUsuarioRepository : IUsuarioRepository
{
    private readonly Dictionary<Guid, Usuario> _usuariosPorId = new();

    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(_usuariosPorId.Values.SingleOrDefault(u => u.Email == email));

    public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_usuariosPorId.GetValueOrDefault(id));

    public Task AdicionarAsync(Usuario usuario, CancellationToken ct)
    {
        _usuariosPorId[usuario.Id] = usuario;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Usuario usuario, CancellationToken ct)
    {
        _usuariosPorId[usuario.Id] = usuario;
        return Task.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Auth/AuthServiceTests.cs` (uses the REAL Fase 1 `PasswordHasher`/`JwtTokenGenerator` — they're cheap, deterministic-enough, and already unit-tested on their own; re-implementing fakes for them here would just duplicate Fase 1's coverage):

```csharp
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Domain.Exceptions;
using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class AuthServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "chave-de-teste-com-pelo-menos-32-caracteres",
        Issuer = "RedeStore.Tests",
        Audience = "RedeStore.Tests.Clients",
    };

    private readonly FakeUsuarioRepository _repositorio = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(
            _repositorio,
            new PasswordHasher(),
            new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options)));
    }

    [Fact]
    public async Task CadastrarAsync_ComEmailNovo_CriaUsuarioComPapelJovem()
    {
        var request = new CadastroRequest("Fulano", "fulano@teste.com", "senha12345");

        var resposta = await _sut.CadastrarAsync(request, CancellationToken.None);

        Assert.Equal("jovem", resposta.Usuario.Papel);
        Assert.Equal("fulano@teste.com", resposta.Usuario.Email);
        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task CadastrarAsync_ComEmailJaExistente_LancaEmailEmUsoException()
    {
        var request = new CadastroRequest("Fulano", "fulano@teste.com", "senha12345");
        await _sut.CadastrarAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<EmailEmUsoException>(() => _sut.CadastrarAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ComCredenciaisCorretas_RetornaToken()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano2@teste.com", "senha12345"), CancellationToken.None);

        var resposta = await _sut.LoginAsync(new LoginRequest("fulano2@teste.com", "senha12345"), CancellationToken.None);

        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task LoginAsync_ComSenhaErrada_LancaCredenciaisInvalidasException()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano3@teste.com", "senha12345"), CancellationToken.None);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            _sut.LoginAsync(new LoginRequest("fulano3@teste.com", "senhaErrada"), CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ComEmailInexistente_LancaCredenciaisInvalidasException()
    {
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            _sut.LoginAsync(new LoginRequest("naoexiste@teste.com", "qualquercoisa"), CancellationToken.None));
    }

    [Fact]
    public async Task ObterComAutorizacaoAsync_ComIdDiferenteENaoAdmin_LancaAcessoNegadoException()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano4@teste.com", "senha12345"), CancellationToken.None);
        var outroId = Guid.NewGuid();

        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _sut.ObterComAutorizacaoAsync(cadastro.Usuario.Id, outroId, ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task ObterComAutorizacaoAsync_ComAdmin_RetornaUsuarioMesmoSemSerODono()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano5@teste.com", "senha12345"), CancellationToken.None);
        var idAdmin = Guid.NewGuid();

        var resultado = await _sut.ObterComAutorizacaoAsync(cadastro.Usuario.Id, idAdmin, ehAdmin: true, CancellationToken.None);

        Assert.Equal(cadastro.Usuario.Id, resultado.Id);
    }

    [Fact]
    public async Task AtualizarPerfilAsync_ComNovoEmailJaEmUso_LancaEmailEmUsoException()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano6@teste.com", "senha12345"), CancellationToken.None);
        var cadastro2 = await _sut.CadastrarAsync(new CadastroRequest("Ciclano", "ciclano6@teste.com", "senha12345"), CancellationToken.None);

        await Assert.ThrowsAsync<EmailEmUsoException>(() =>
            _sut.AtualizarPerfilAsync(cadastro2.Usuario.Id, new AtualizarPerfilRequest(null, "fulano6@teste.com", null), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarPerfilAsync_ComNomeNovo_AtualizaEPersisteNoRepositorio()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano7@teste.com", "senha12345"), CancellationToken.None);

        var atualizado = await _sut.AtualizarPerfilAsync(cadastro.Usuario.Id, new AtualizarPerfilRequest("Novo Nome", null, null), CancellationToken.None);

        Assert.Equal("Novo Nome", atualizado.Nome);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~AuthServiceTests`
Expected: FAIL to compile — DTOs, `IAuthService`, `AuthService` don't exist yet.

- [ ] **Step 3: Create the DTOs**

Create `src/RedeStore.Application/Auth/Dtos/UsuarioDto.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record UsuarioDto(Guid Id, string Nome, string Email, string? Telefone, string Papel);
```

Create `src/RedeStore.Application/Auth/Dtos/CadastroRequest.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record CadastroRequest(string Nome, string Email, string Senha);
```

Create `src/RedeStore.Application/Auth/Dtos/LoginRequest.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record LoginRequest(string Email, string Senha);
```

Create `src/RedeStore.Application/Auth/Dtos/AuthResponse.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record AuthResponse(UsuarioDto Usuario, string Token);
```

Create `src/RedeStore.Application/Auth/Dtos/AtualizarPerfilRequest.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record AtualizarPerfilRequest(string? Nome, string? Email, string? Telefone);
```

- [ ] **Step 4: Create the validators**

Create `src/RedeStore.Application/Auth/Validators/CadastroRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class CadastroRequestValidator : AbstractValidator<CadastroRequest>
{
    public CadastroRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty().MinimumLength(2);
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Senha).NotEmpty().MinimumLength(8);
    }
}
```

Create `src/RedeStore.Application/Auth/Validators/LoginRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Senha).NotEmpty();
    }
}
```

Create `src/RedeStore.Application/Auth/Validators/AtualizarPerfilRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class AtualizarPerfilRequestValidator : AbstractValidator<AtualizarPerfilRequest>
{
    public AtualizarPerfilRequestValidator()
    {
        RuleFor(r => r.Nome).MinimumLength(2).When(r => r.Nome is not null);
        RuleFor(r => r.Email).EmailAddress().When(r => r.Email is not null);
    }
}
```

Note: `FluentValidation`'s package reference lives on `RedeStore.Api` (added in Task 3). `RedeStore.Application` does not yet reference the `FluentValidation` NuGet package directly — add it here too:

```bash
dotnet add src/RedeStore.Application/RedeStore.Application.csproj package FluentValidation
```

- [ ] **Step 5: Implement `IAuthService` and `AuthService`**

Create `src/RedeStore.Application/Auth/IAuthService.cs`:

```csharp
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct);
    Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest request, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Auth/AuthService.cs`:

```csharp
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthService(IUsuarioRepository usuarioRepository, IPasswordHasher passwordHasher, IJwtTokenGenerator jwtTokenGenerator)
    {
        _usuarioRepository = usuarioRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct)
    {
        var existente = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
        if (existente is not null)
        {
            throw new EmailEmUsoException($"O e-mail '{request.Email}' já está em uso.");
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Email = request.Email,
            Papel = Papel.Jovem,
            SenhaHash = _passwordHasher.HashPassword(request.Senha),
        };

        await _usuarioRepository.AdicionarAsync(usuario, ct);

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
        if (usuario is null || !_passwordHasher.VerifyPassword(usuario.SenhaHash, request.Senha))
        {
            throw new CredenciaisInvalidasException("E-mail ou senha inválidos.");
        }

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        return MapearParaDto(usuario);
    }

    public async Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct)
    {
        if (idSolicitado != idUsuarioLogado && !ehAdmin)
        {
            throw new AcessoNegadoException("Você não tem permissão para ver este usuário.");
        }
        return await ObterPorIdAsync(idSolicitado, ct);
    }

    public async Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");

        if (request.Email is not null && request.Email != usuario.Email)
        {
            var existente = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
            if (existente is not null)
            {
                throw new EmailEmUsoException($"O e-mail '{request.Email}' já está em uso.");
            }
            usuario.Email = request.Email;
        }

        if (request.Nome is not null)
        {
            usuario.Nome = request.Nome;
        }

        if (request.Telefone is not null)
        {
            usuario.Telefone = request.Telefone;
        }

        await _usuarioRepository.AtualizarAsync(usuario, ct);
        return MapearParaDto(usuario);
    }

    private static UsuarioDto MapearParaDto(Usuario usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Papel.ToString().ToLowerInvariant());
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~AuthServiceTests`
Expected: 9 passed.

- [ ] **Step 7: Register the service and validators in DI**

Open `src/RedeStore.Api/Program.cs`. Add these `using` statements:

```csharp
using FluentValidation;
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Auth.Validators;
```

Add these registrations right after the `IPasswordResetTokenRepository` registration from Task 2:

```csharp
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IValidator<CadastroRequest>, CadastroRequestValidator>();
builder.Services.AddSingleton<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddSingleton<IValidator<AtualizarPerfilRequest>, AtualizarPerfilRequestValidator>();
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/RedeStore.Application/Auth/ src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Auth/
git commit -m "Add AuthService covering cadastro, login, perfil and self-or-admin lookup"
```

---

## Task 6: Password Reset Flow (Resend E-mail)

**Files:**
- Create: `src/RedeStore.Application/Common/IEmailSender.cs`
- Create: `src/RedeStore.Application/Common/FrontendOptions.cs`
- Create: `src/RedeStore.Application/Auth/Dtos/RecuperarSenhaRequest.cs`
- Create: `src/RedeStore.Application/Auth/Dtos/RedefinirSenhaRequest.cs`
- Create: `src/RedeStore.Application/Auth/Validators/RecuperarSenhaRequestValidator.cs`
- Create: `src/RedeStore.Application/Auth/Validators/RedefinirSenhaRequestValidator.cs`
- Modify: `src/RedeStore.Application/Auth/IAuthService.cs`
- Modify: `src/RedeStore.Application/Auth/AuthService.cs`
- Create: `src/RedeStore.Infrastructure/Email/ResendOptions.cs`
- Create: `src/RedeStore.Infrastructure/Email/ResendEmailSender.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Modify: `tests/RedeStore.IntegrationTests/ApiFactory.cs`
- Create: `tests/RedeStore.IntegrationTests/FakeEmailSender.cs`
- Create: `tests/RedeStore.UnitTests/Auth/FakePasswordResetTokenRepository.cs`
- Create: `tests/RedeStore.UnitTests/Auth/FakeEmailSender.cs`
- Modify: `tests/RedeStore.UnitTests/Auth/AuthServiceTests.cs`

**Interfaces:**
- Consumes: `IPasswordResetTokenRepository` (Task 2), `TokenInvalidoException` (Task 4), `AuthService`'s existing constructor deps (Task 5).
- Produces: `RedeStore.Application.Common.IEmailSender.EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)`. `IAuthService` gains `Task RecuperarSenhaAsync(string email, CancellationToken ct)` and `Task RedefinirSenhaAsync(string token, string novaSenha, CancellationToken ct)`. `ApiFactory` gains a public `EmailSender` property of type `FakeEmailSender` — Task 7 and Task 8's integration tests read `_factory.EmailSender.Enviados` to extract the reset token instead of calling the real Resend API.

- [ ] **Step 1: Write the failing unit tests**

Create `tests/RedeStore.UnitTests/Auth/FakePasswordResetTokenRepository.cs`:

```csharp
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Auth;

public sealed class FakePasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly Dictionary<string, PasswordResetToken> _tokensPorHash = new();

    public Task AdicionarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _tokensPorHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }

    public Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Task.FromResult(_tokensPorHash.GetValueOrDefault(tokenHash));

    public Task AtualizarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _tokensPorHash[token.TokenHash] = token;
        return Task.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Auth/FakeEmailSender.cs`:

```csharp
using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Auth;

public sealed class FakeEmailSender : IEmailSender
{
    public List<(string Destinatario, string Assunto, string CorpoHtml)> Enviados { get; } = new();

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)
    {
        Enviados.Add((destinatario, assunto, corpoHtml));
        return Task.CompletedTask;
    }
}
```

Open `tests/RedeStore.UnitTests/Auth/AuthServiceTests.cs` and replace its entire contents with (this is Task 5's file plus the constructor change and 5 new tests — every existing test stays, only the constructor call and fields change):

```csharp
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Exceptions;
using RedeStore.Infrastructure.Auth;
using Xunit;

namespace RedeStore.UnitTests.Auth;

public class AuthServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        SigningKey = "chave-de-teste-com-pelo-menos-32-caracteres",
        Issuer = "RedeStore.Tests",
        Audience = "RedeStore.Tests.Clients",
    };

    private readonly FakeUsuarioRepository _repositorio = new();
    private readonly FakePasswordResetTokenRepository _tokenRepositorio = new();
    private readonly FakeEmailSender _emailSender = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(
            _repositorio,
            _tokenRepositorio,
            new PasswordHasher(),
            new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options)),
            _emailSender,
            Microsoft.Extensions.Options.Options.Create(new FrontendOptions { ResetPasswordUrl = "http://localhost:4200/redefinir-senha" }));
    }

    [Fact]
    public async Task CadastrarAsync_ComEmailNovo_CriaUsuarioComPapelJovem()
    {
        var request = new CadastroRequest("Fulano", "fulano@teste.com", "senha12345");

        var resposta = await _sut.CadastrarAsync(request, CancellationToken.None);

        Assert.Equal("jovem", resposta.Usuario.Papel);
        Assert.Equal("fulano@teste.com", resposta.Usuario.Email);
        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task CadastrarAsync_ComEmailJaExistente_LancaEmailEmUsoException()
    {
        var request = new CadastroRequest("Fulano", "fulano@teste.com", "senha12345");
        await _sut.CadastrarAsync(request, CancellationToken.None);

        await Assert.ThrowsAsync<EmailEmUsoException>(() => _sut.CadastrarAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ComCredenciaisCorretas_RetornaToken()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano2@teste.com", "senha12345"), CancellationToken.None);

        var resposta = await _sut.LoginAsync(new LoginRequest("fulano2@teste.com", "senha12345"), CancellationToken.None);

        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task LoginAsync_ComSenhaErrada_LancaCredenciaisInvalidasException()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano3@teste.com", "senha12345"), CancellationToken.None);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            _sut.LoginAsync(new LoginRequest("fulano3@teste.com", "senhaErrada"), CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ComEmailInexistente_LancaCredenciaisInvalidasException()
    {
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() =>
            _sut.LoginAsync(new LoginRequest("naoexiste@teste.com", "qualquercoisa"), CancellationToken.None));
    }

    [Fact]
    public async Task ObterComAutorizacaoAsync_ComIdDiferenteENaoAdmin_LancaAcessoNegadoException()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano4@teste.com", "senha12345"), CancellationToken.None);
        var outroId = Guid.NewGuid();

        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _sut.ObterComAutorizacaoAsync(cadastro.Usuario.Id, outroId, ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task ObterComAutorizacaoAsync_ComAdmin_RetornaUsuarioMesmoSemSerODono()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano5@teste.com", "senha12345"), CancellationToken.None);
        var idAdmin = Guid.NewGuid();

        var resultado = await _sut.ObterComAutorizacaoAsync(cadastro.Usuario.Id, idAdmin, ehAdmin: true, CancellationToken.None);

        Assert.Equal(cadastro.Usuario.Id, resultado.Id);
    }

    [Fact]
    public async Task AtualizarPerfilAsync_ComNovoEmailJaEmUso_LancaEmailEmUsoException()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano6@teste.com", "senha12345"), CancellationToken.None);
        var cadastro2 = await _sut.CadastrarAsync(new CadastroRequest("Ciclano", "ciclano6@teste.com", "senha12345"), CancellationToken.None);

        await Assert.ThrowsAsync<EmailEmUsoException>(() =>
            _sut.AtualizarPerfilAsync(cadastro2.Usuario.Id, new AtualizarPerfilRequest(null, "fulano6@teste.com", null), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarPerfilAsync_ComNomeNovo_AtualizaEPersisteNoRepositorio()
    {
        var cadastro = await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano7@teste.com", "senha12345"), CancellationToken.None);

        var atualizado = await _sut.AtualizarPerfilAsync(cadastro.Usuario.Id, new AtualizarPerfilRequest("Novo Nome", null, null), CancellationToken.None);

        Assert.Equal("Novo Nome", atualizado.Nome);
    }

    [Fact]
    public async Task RecuperarSenhaAsync_ComEmailExistente_EnviaEmailComLink()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano8@teste.com", "senha12345"), CancellationToken.None);

        await _sut.RecuperarSenhaAsync("fulano8@teste.com", CancellationToken.None);

        Assert.Single(_emailSender.Enviados);
        Assert.Equal("fulano8@teste.com", _emailSender.Enviados[0].Destinatario);
        Assert.Contains("token=", _emailSender.Enviados[0].CorpoHtml);
    }

    [Fact]
    public async Task RecuperarSenhaAsync_ComEmailInexistente_NaoEnviaEmailENaoLancaExcecao()
    {
        await _sut.RecuperarSenhaAsync("naoexiste@teste.com", CancellationToken.None);

        Assert.Empty(_emailSender.Enviados);
    }

    [Fact]
    public async Task RedefinirSenhaAsync_ComTokenValido_AtualizaSenhaEPermiteLoginComNovaSenha()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano9@teste.com", "senha12345"), CancellationToken.None);
        await _sut.RecuperarSenhaAsync("fulano9@teste.com", CancellationToken.None);
        var token = ExtrairTokenDoLink(_emailSender.Enviados[0].CorpoHtml);

        await _sut.RedefinirSenhaAsync(token, "senhaNova123", CancellationToken.None);
        var resposta = await _sut.LoginAsync(new LoginRequest("fulano9@teste.com", "senhaNova123"), CancellationToken.None);

        Assert.NotEmpty(resposta.Token);
    }

    [Fact]
    public async Task RedefinirSenhaAsync_ComTokenUsadoDuasVezes_LancaTokenInvalidoNaSegundaVez()
    {
        await _sut.CadastrarAsync(new CadastroRequest("Fulano", "fulano10@teste.com", "senha12345"), CancellationToken.None);
        await _sut.RecuperarSenhaAsync("fulano10@teste.com", CancellationToken.None);
        var token = ExtrairTokenDoLink(_emailSender.Enviados[0].CorpoHtml);
        await _sut.RedefinirSenhaAsync(token, "senhaNova123", CancellationToken.None);

        await Assert.ThrowsAsync<TokenInvalidoException>(() =>
            _sut.RedefinirSenhaAsync(token, "outraSenha123", CancellationToken.None));
    }

    [Fact]
    public async Task RedefinirSenhaAsync_ComTokenInexistente_LancaTokenInvalidoException()
    {
        await Assert.ThrowsAsync<TokenInvalidoException>(() =>
            _sut.RedefinirSenhaAsync("token-que-nao-existe", "outraSenha123", CancellationToken.None));
    }

    private static string ExtrairTokenDoLink(string corpoHtml)
    {
        var inicio = corpoHtml.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var fim = corpoHtml.IndexOf('"', inicio);
        return corpoHtml[inicio..fim];
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~AuthServiceTests`
Expected: FAIL to compile — `IPasswordResetTokenRepository`/`IEmailSender`/`FrontendOptions` constructor args and the new methods don't exist on `AuthService` yet.

- [ ] **Step 3: Add `IEmailSender` and `FrontendOptions`**

Create `src/RedeStore.Application/Common/IEmailSender.cs`:

```csharp
namespace RedeStore.Application.Common;

public interface IEmailSender
{
    Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Common/FrontendOptions.cs`:

```csharp
namespace RedeStore.Application.Common;

public sealed class FrontendOptions
{
    public const string SectionName = "Frontend";

    public required string ResetPasswordUrl { get; init; }
}
```

`FrontendOptions` needs the `Microsoft.Extensions.Options` package on `RedeStore.Application` (a lightweight, framework-agnostic package — not an ASP.NET Core-specific one, so this doesn't violate the layering rule):

```bash
dotnet add src/RedeStore.Application/RedeStore.Application.csproj package Microsoft.Extensions.Options
```

- [ ] **Step 4: Add the new DTOs and validators**

Create `src/RedeStore.Application/Auth/Dtos/RecuperarSenhaRequest.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record RecuperarSenhaRequest(string Email);
```

Create `src/RedeStore.Application/Auth/Dtos/RedefinirSenhaRequest.cs`:

```csharp
namespace RedeStore.Application.Auth.Dtos;

public sealed record RedefinirSenhaRequest(string Token, string NovaSenha);
```

Create `src/RedeStore.Application/Auth/Validators/RecuperarSenhaRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class RecuperarSenhaRequestValidator : AbstractValidator<RecuperarSenhaRequest>
{
    public RecuperarSenhaRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
    }
}
```

Create `src/RedeStore.Application/Auth/Validators/RedefinirSenhaRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth.Validators;

public sealed class RedefinirSenhaRequestValidator : AbstractValidator<RedefinirSenhaRequest>
{
    public RedefinirSenhaRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty();
        RuleFor(r => r.NovaSenha).NotEmpty().MinimumLength(8);
    }
}
```

- [ ] **Step 5: Extend `IAuthService` and `AuthService`**

Open `src/RedeStore.Application/Auth/IAuthService.cs` and replace its contents with:

```csharp
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct);
    Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest request, CancellationToken ct);
    Task RecuperarSenhaAsync(string email, CancellationToken ct);
    Task RedefinirSenhaAsync(string token, string novaSenha, CancellationToken ct);
}
```

Open `src/RedeStore.Application/Auth/AuthService.cs` and replace its contents with:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Auth;

public sealed class AuthService : IAuthService
{
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IEmailSender _emailSender;
    private readonly FrontendOptions _frontendOptions;

    public AuthService(
        IUsuarioRepository usuarioRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IEmailSender emailSender,
        IOptions<FrontendOptions> frontendOptions)
    {
        _usuarioRepository = usuarioRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _emailSender = emailSender;
        _frontendOptions = frontendOptions.Value;
    }

    public async Task<AuthResponse> CadastrarAsync(CadastroRequest request, CancellationToken ct)
    {
        var existente = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
        if (existente is not null)
        {
            throw new EmailEmUsoException($"O e-mail '{request.Email}' já está em uso.");
        }

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Email = request.Email,
            Papel = Papel.Jovem,
            SenhaHash = _passwordHasher.HashPassword(request.Senha),
        };

        await _usuarioRepository.AdicionarAsync(usuario, ct);

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
        if (usuario is null || !_passwordHasher.VerifyPassword(usuario.SenhaHash, request.Senha))
        {
            throw new CredenciaisInvalidasException("E-mail ou senha inválidos.");
        }

        var token = _jwtTokenGenerator.GenerateToken(usuario.Id, usuario.Email, usuario.Papel.ToString().ToLowerInvariant());
        return new AuthResponse(MapearParaDto(usuario), token);
    }

    public async Task<UsuarioDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("Usuário não encontrado.");
        return MapearParaDto(usuario);
    }

    public async Task<UsuarioDto> ObterComAutorizacaoAsync(Guid idSolicitado, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct)
    {
        if (idSolicitado != idUsuarioLogado && !ehAdmin)
        {
            throw new AcessoNegadoException("Você não tem permissão para ver este usuário.");
        }
        return await ObterPorIdAsync(idSolicitado, ct);
    }

    public async Task<UsuarioDto> AtualizarPerfilAsync(Guid usuarioId, AtualizarPerfilRequest request, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("Usuário autenticado não encontrado.");

        if (request.Email is not null && request.Email != usuario.Email)
        {
            var existente = await _usuarioRepository.BuscarPorEmailAsync(request.Email, ct);
            if (existente is not null)
            {
                throw new EmailEmUsoException($"O e-mail '{request.Email}' já está em uso.");
            }
            usuario.Email = request.Email;
        }

        if (request.Nome is not null)
        {
            usuario.Nome = request.Nome;
        }

        if (request.Telefone is not null)
        {
            usuario.Telefone = request.Telefone;
        }

        await _usuarioRepository.AtualizarAsync(usuario, ct);
        return MapearParaDto(usuario);
    }

    public async Task RecuperarSenhaAsync(string email, CancellationToken ct)
    {
        var usuario = await _usuarioRepository.BuscarPorEmailAsync(email, ct);
        if (usuario is null)
        {
            return;
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var tokenBruto = Convert.ToBase64String(tokenBytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenBruto)));

        await _passwordResetTokenRepository.AdicionarAsync(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuario.Id,
            TokenHash = tokenHash,
            ExpiraEm = DateTime.UtcNow.AddHours(1),
        }, ct);

        var link = $"{_frontendOptions.ResetPasswordUrl}?token={tokenBruto}";
        await _emailSender.EnviarAsync(
            usuario.Email,
            "Recuperação de senha — REDE",
            $"<p>Clique no link para redefinir sua senha: <a href=\"{link}\">{link}</a></p><p>Este link expira em 1 hora.</p>",
            ct);
    }

    public async Task RedefinirSenhaAsync(string token, string novaSenha, CancellationToken ct)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var resetToken = await _passwordResetTokenRepository.BuscarPorTokenHashAsync(tokenHash, ct);

        if (resetToken is null || resetToken.UsadoEm is not null || resetToken.ExpiraEm < DateTime.UtcNow)
        {
            throw new TokenInvalidoException("Token de redefinição de senha inválido ou expirado.");
        }

        var usuario = await _usuarioRepository.BuscarPorIdAsync(resetToken.UsuarioId, ct)
            ?? throw new TokenInvalidoException("Token de redefinição de senha inválido ou expirado.");

        usuario.SenhaHash = _passwordHasher.HashPassword(novaSenha);
        await _usuarioRepository.AtualizarAsync(usuario, ct);

        resetToken.UsadoEm = DateTime.UtcNow;
        await _passwordResetTokenRepository.AtualizarAsync(resetToken, ct);
    }

    private static UsuarioDto MapearParaDto(Usuario usuario) =>
        new(usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Papel.ToString().ToLowerInvariant());
}
```

- [ ] **Step 6: Run the unit tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter FullyQualifiedName~AuthServiceTests`
Expected: 14 passed.

- [ ] **Step 7: Implement `ResendEmailSender`**

Create `src/RedeStore.Infrastructure/Email/ResendOptions.cs`:

```csharp
namespace RedeStore.Infrastructure.Email;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    public required string ApiKey { get; init; }
    public required string FromEmail { get; init; }
}
```

Create `src/RedeStore.Infrastructure/Email/ResendEmailSender.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Email;

public sealed class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ResendOptions _options;

    public ResendEmailSender(HttpClient httpClient, IOptions<ResendOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)
    {
        var payload = new
        {
            from = _options.FromEmail,
            to = new[] { destinatario },
            subject = assunto,
            html = corpoHtml,
        };

        var response = await _httpClient.PostAsJsonAsync("emails", payload, ct);
        response.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 8: Wire it all into `Program.cs` and set up Resend secrets**

Open `src/RedeStore.Api/Program.cs`. It already has `using RedeStore.Application.Auth.Dtos;` and `using RedeStore.Application.Auth.Validators;` from Task 5 — do not duplicate those. Add only these two new `using` statements:

```csharp
using System.Net.Http.Headers;
using RedeStore.Infrastructure.Email;
```

Add these registrations right after the `IValidator<AtualizarPerfilRequest>` registration from Task 5:

```csharp
builder.Services.AddSingleton<IValidator<RecuperarSenhaRequest>, RecuperarSenhaRequestValidator>();
builder.Services.AddSingleton<IValidator<RedefinirSenhaRequest>, RedefinirSenhaRequestValidator>();

builder.Services.Configure<FrontendOptions>(builder.Configuration.GetSection(FrontendOptions.SectionName));
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection(ResendOptions.SectionName));

var resendApiKey = builder.Configuration["Resend:ApiKey"]
    ?? throw new InvalidOperationException("Resend:ApiKey ausente. Configure via dotnet user-secrets.");

builder.Services.AddHttpClient<IEmailSender, ResendEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.resend.com/");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resendApiKey);
});
```

Set the dev secrets (use a real Resend API key if you have one; any non-empty string works for now since Fase 2's own tests never call the real API):

```bash
dotnet user-secrets set "Resend:ApiKey" "re_sua_chave_aqui" --project src/RedeStore.Api
dotnet user-secrets set "Resend:FromEmail" "onboarding@resend.dev" --project src/RedeStore.Api
dotnet user-secrets set "Frontend:ResetPasswordUrl" "http://localhost:4200/redefinir-senha" --project src/RedeStore.Api
```

- [ ] **Step 9: Substitute `IEmailSender` with a fake in the integration test harness**

Create `tests/RedeStore.IntegrationTests/FakeEmailSender.cs`:

```csharp
using System.Collections.Concurrent;
using RedeStore.Application.Common;

namespace RedeStore.IntegrationTests;

public sealed class FakeEmailSender : IEmailSender
{
    public ConcurrentBag<(string Destinatario, string Assunto, string CorpoHtml)> Enviados { get; } = new();

    public Task EnviarAsync(string destinatario, string assunto, string corpoHtml, CancellationToken ct)
    {
        Enviados.Add((destinatario, assunto, corpoHtml));
        return Task.CompletedTask;
    }
}
```

Open `tests/RedeStore.IntegrationTests/ApiFactory.cs` and replace its entire contents with:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RedeStore.Application.Common;
using RedeStore.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace RedeStore.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public FakeEmailSender EmailSender =>
        (FakeEmailSender)Services.GetRequiredService<IEmailSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:SigningKey"] = "chave-de-teste-para-integracao-com-pelo-menos-32-caracteres",
                ["Jwt:Issuer"] = "RedeStore.IntegrationTests",
                ["Jwt:Audience"] = "RedeStore.IntegrationTests.Clients",
                ["Resend:ApiKey"] = "chave-fake-para-testes",
                ["Resend:FromEmail"] = "nao-responda@teste.com",
                ["Frontend:ResetPasswordUrl"] = "http://localhost:4200/redefinir-senha",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, FakeEmailSender>();
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RedeStoreDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

- [ ] **Step 10: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions. (The integration suite never touches the real Resend API — `ConfigureServices` always substitutes `FakeEmailSender`.)

- [ ] **Step 11: Commit**

```bash
git add src/RedeStore.Application/Common/IEmailSender.cs src/RedeStore.Application/Common/FrontendOptions.cs src/RedeStore.Application/Auth/ src/RedeStore.Application/RedeStore.Application.csproj src/RedeStore.Infrastructure/Email/ src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/ApiFactory.cs tests/RedeStore.IntegrationTests/FakeEmailSender.cs tests/RedeStore.UnitTests/Auth/
git commit -m "Add password reset flow via Resend, with fake e-mail sender for tests"
```

---

## Task 7: Map the 7 Endpoints

**Files:**
- Create: `src/RedeStore.Api/Endpoints/AuthEndpoints.cs`
- Create: `src/RedeStore.Api/Endpoints/UsuariosEndpoints.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.IntegrationTests/Auth/AuthEndpointsSmokeTests.cs`

**Interfaces:**
- Consumes: `IAuthService` (Task 5/6), `ValidationFilter<T>` (Task 3), all DTOs from Tasks 5-6.
- Produces: the 7 HTTP routes from the spec (section 4) — `POST /auth/cadastro`, `POST /auth/login`, `GET /auth/me`, `PATCH /auth/perfil`, `GET /usuarios/{id:guid}`, `POST /auth/recuperar-senha`, `POST /auth/redefinir-senha`. Task 8's end-to-end tests exercise all of them together.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Auth/AuthEndpointsSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using RedeStore.Application.Auth.Dtos;
using Xunit;

namespace RedeStore.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public class AuthEndpointsSmokeTests
{
    private readonly HttpClient _client;

    public AuthEndpointsSmokeTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostCadastro_ComDadosValidos_Retorna200ComToken()
    {
        var request = new CadastroRequest("Fulano", $"{Guid.NewGuid()}@teste.com", "senha12345");

        var response = await _client.PostAsJsonAsync("/auth/cadastro", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var corpo = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotEmpty(corpo!.Token);
    }

    [Fact]
    public async Task GetMe_SemToken_Retorna401()
    {
        var response = await _client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCadastro_ComEmailInvalido_Retorna400()
    {
        var request = new CadastroRequest("Fulano", "nao-e-um-email", "senha12345");

        var response = await _client.PostAsJsonAsync("/auth/cadastro", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~AuthEndpointsSmokeTests`
Expected: FAIL — routes return 404, since no endpoints are mapped yet.

- [ ] **Step 3: Implement the endpoint groups**

Create `src/RedeStore.Api/Endpoints/AuthEndpoints.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Api.Filters;
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/auth");

        grupo.MapPost("/cadastro", async (CadastroRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var resposta = await authService.CadastrarAsync(request, ct);
            return Results.Ok(resposta);
        }).AddEndpointFilter<ValidationFilter<CadastroRequest>>();

        grupo.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var resposta = await authService.LoginAsync(request, ct);
            return Results.Ok(resposta);
        }).AddEndpointFilter<ValidationFilter<LoginRequest>>();

        grupo.MapGet("/me", async (ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var usuario = await authService.ObterPorIdAsync(id, ct);
            return Results.Ok(usuario);
        }).RequireAuthorization();

        grupo.MapPatch("/perfil", async (AtualizarPerfilRequest request, ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var usuario = await authService.AtualizarPerfilAsync(id, request, ct);
            return Results.Ok(usuario);
        }).RequireAuthorization().AddEndpointFilter<ValidationFilter<AtualizarPerfilRequest>>();

        grupo.MapPost("/recuperar-senha", async (RecuperarSenhaRequest request, IAuthService authService, CancellationToken ct) =>
        {
            await authService.RecuperarSenhaAsync(request.Email, ct);
            return Results.NoContent();
        }).AddEndpointFilter<ValidationFilter<RecuperarSenhaRequest>>();

        grupo.MapPost("/redefinir-senha", async (RedefinirSenhaRequest request, IAuthService authService, CancellationToken ct) =>
        {
            await authService.RedefinirSenhaAsync(request.Token, request.NovaSenha, ct);
            return Results.NoContent();
        }).AddEndpointFilter<ValidationFilter<RedefinirSenhaRequest>>();
    }
}
```

Create `src/RedeStore.Api/Endpoints/UsuariosEndpoints.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Application.Auth;

namespace RedeStore.Api.Endpoints;

public static class UsuariosEndpoints
{
    public static void MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/usuarios/{id:guid}", async (Guid id, ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var ehAdmin = user.IsInRole("admin");
            var usuario = await authService.ObterComAutorizacaoAsync(id, idUsuarioLogado, ehAdmin, ct);
            return Results.Ok(usuario);
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 4: Map the endpoint groups in `Program.cs`**

Open `src/RedeStore.Api/Program.cs`. Add this `using`:

```csharp
using RedeStore.Api.Endpoints;
```

Add these two lines right after the `/health` endpoint mapping, before `app.Run();`:

```csharp
app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~AuthEndpointsSmokeTests`
Expected: 3 passed.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/RedeStore.Api/Endpoints/ src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Auth/AuthEndpointsSmokeTests.cs
git commit -m "Map the 7 auth/usuarios endpoints"
```

---

## Task 8: End-to-End Tests Covering the Fase 2 Definition of Done

**Files:**
- Test: `tests/RedeStore.IntegrationTests/Auth/AuthFlowEndToEndTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-7 — this task adds no production code, only tests that exercise the whole phase together against the real HTTP pipeline.

- [ ] **Step 1: Write the end-to-end test**

Create `tests/RedeStore.IntegrationTests/Auth/AuthFlowEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Auth;

[Collection(IntegrationTestCollection.Name)]
public class AuthFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowEndToEndTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FluxoCompleto_CadastroLoginPerfilEsqueciSenhaRedefinir_FuncionaPontaAPonta()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        var senhaOriginal = "senhaOriginal123";

        var cadastroResponse = await _client.PostAsJsonAsync("/auth/cadastro",
            new CadastroRequest("Fulano de Tal", email, senhaOriginal));
        cadastroResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, senhaOriginal));
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        using var clienteAutenticado = _factory.CreateClient();
        clienteAutenticado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var meResponse = await clienteAutenticado.GetAsync("/auth/me");
        meResponse.EnsureSuccessStatusCode();
        var me = await meResponse.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.Equal(email, me!.Email);

        var perfilResponse = await clienteAutenticado.PatchAsJsonAsync("/auth/perfil",
            new AtualizarPerfilRequest("Nome Atualizado", null, null));
        perfilResponse.EnsureSuccessStatusCode();
        var perfil = await perfilResponse.Content.ReadFromJsonAsync<UsuarioDto>();
        Assert.Equal("Nome Atualizado", perfil!.Nome);

        var usuarioProprioResponse = await clienteAutenticado.GetAsync($"/usuarios/{me.Id}");
        usuarioProprioResponse.EnsureSuccessStatusCode();

        var outroId = Guid.NewGuid();
        var usuarioOutroResponse = await clienteAutenticado.GetAsync($"/usuarios/{outroId}");
        Assert.Equal(HttpStatusCode.Forbidden, usuarioOutroResponse.StatusCode);

        var recuperarResponse = await _client.PostAsJsonAsync("/auth/recuperar-senha", new RecuperarSenhaRequest(email));
        Assert.Equal(HttpStatusCode.NoContent, recuperarResponse.StatusCode);

        var emailEnviado = Assert.Single(_factory.EmailSender.Enviados, e => e.Destinatario == email);
        var token = ExtrairTokenDoLink(emailEnviado.CorpoHtml);

        var redefinirResponse = await _client.PostAsJsonAsync("/auth/redefinir-senha",
            new RedefinirSenhaRequest(token, "senhaNova123"));
        Assert.Equal(HttpStatusCode.NoContent, redefinirResponse.StatusCode);

        var novoLoginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senhaNova123"));
        novoLoginResponse.EnsureSuccessStatusCode();

        var loginAntigoResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, senhaOriginal));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAntigoResponse.StatusCode);

        var redefinirDeNovoResponse = await _client.PostAsJsonAsync("/auth/redefinir-senha",
            new RedefinirSenhaRequest(token, "outraSenha123"));
        Assert.Equal(HttpStatusCode.BadRequest, redefinirDeNovoResponse.StatusCode);
    }

    [Fact]
    public async Task PostCadastro_ComEmailJaCadastrado_Retorna409()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Fulano", email, "senha12345"));

        var response = await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Fulano", email, "senha12345"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetUsuarioPorId_ComoAdmin_PermiteVerOutroUsuario()
    {
        var emailAlvo = $"{Guid.NewGuid()}@teste.com";
        var cadastroAlvoResponse = await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Alvo", emailAlvo, "senha12345"));
        var alvo = await cadastroAlvoResponse.Content.ReadFromJsonAsync<AuthResponse>();

        var emailAdmin = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Admin", emailAdmin, "senha12345"));

        using (var scope = _factory.Services.CreateScope())
        {
            var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
            var admin = await usuarioRepositorio.BuscarPorEmailAsync(emailAdmin, CancellationToken.None);
            admin!.Papel = Papel.Admin;
            await usuarioRepositorio.AtualizarAsync(admin, CancellationToken.None);
        }

        var loginAdminResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(emailAdmin, "senha12345"));
        var loginAdmin = await loginAdminResponse.Content.ReadFromJsonAsync<AuthResponse>();

        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginAdmin!.Token);

        var response = await clienteAdmin.GetAsync($"/usuarios/{alvo!.Usuario.Id}");

        response.EnsureSuccessStatusCode();
    }

    private static string ExtrairTokenDoLink(string corpoHtml)
    {
        var inicio = corpoHtml.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var fim = corpoHtml.IndexOf('"', inicio);
        return corpoHtml[inicio..fim];
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (if anything from Tasks 1-7 was missed, this is where it surfaces)**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~AuthFlowEndToEndTests`
Expected: if Tasks 1-7 were done correctly, these should mostly pass already — this task is verification, not new implementation. If something fails, fix the gap in the relevant earlier layer (repository, service, or endpoint) rather than adding a workaround here.

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~AuthFlowEndToEndTests`
Expected: 3 passed.

- [ ] **Step 4: Run the entire solution's test suite one final time**

Run: `dotnet test`
Expected: all tests across `RedeStore.UnitTests` and `RedeStore.IntegrationTests` pass.

- [ ] **Step 5: Commit**

```bash
git add tests/RedeStore.IntegrationTests/Auth/AuthFlowEndToEndTests.cs
git commit -m "Add end-to-end tests covering the Fase 2 Definition of Done"
```

---

## Definition of Done (Fase 2)

- `dotnet test` passes for the entire solution, including a true end-to-end run of cadastro → login → `/auth/me` → `/auth/perfil` → `/usuarios/:id` (self and as admin, and 403 for neither) → esqueci a senha → redefinir com token → login com a senha nova → falha ao reusar o token.
- No password or reset token stored in plaintext anywhere (`SenhaHash` via `IPasswordHasher`, `TokenHash` via SHA-256).
- No test ever calls the real Resend API — always the in-memory `FakeEmailSender`.
- The Angular "redefinir senha" page does not exist yet — this is a known, explicit gap for the frontend team, not something this plan builds.

