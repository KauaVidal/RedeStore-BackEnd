# Backend REDE — Fase 4 (Eventos & Inscrições) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Eventos & Inscrições subsystem — public agenda browsing plus admin CRUD for `Evento`, and the full inscription flow (`Inscricao`) with a real pessimistic lock on vagas — on top of the Fase 1/2/3 foundation, matching the 3-outcome (`criada`/`ja_inscrito`/`esgotado`) contract the frontend already expects.

**Architecture:** New `Evento`/`Inscricao` entities and their EF Core migration; `IEventoRepository`/`IInscricaoRepository` interfaces in `Application.Common` (implementations in `Infrastructure.Persistence.Repositories`, per the fixed dependency graph); a new `IUnitOfWork`/`ITransacao` pair in `Application.Common` so `Application` can open/commit a database transaction without referencing EF Core types directly (needed because the vagas lock only serializes correctly inside an explicit transaction); `EventoService` covering catalog CRUD with a computed (never persisted) `VagasRestantes`; `InscricaoService` implementing the lock-then-decide flow from the design doc (section 5) inside that transaction; two new `DomainException` subtypes (`EventoNaoEncontradoException`, `EventoComInscricoesConfirmadasException`, `InscricaoNaoEncontradaException`) following the exact Fase 2/3 shape; the existing generic `ValidationFilter<T>` reused for the two new Evento request DTOs; the existing "self-or-admin" claims pattern from Fase 2 (`UsuariosEndpoints`/`AuthService.ObterComAutorizacaoAsync`) reused for "dono-ou-admin" on inscription cancellation.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (already wired — `Database.SqlQueryRaw<TResult>` used for the raw `FOR UPDATE` lock query), FluentValidation (already wired), `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`, already wired), Testcontainers.PostgreSql (already wired — this is also what proves the pessimistic lock actually serializes concurrent requests, since the mock/in-memory fakes used in unit tests cannot expose that race). No new NuGet packages needed for this phase.

**Spec:** `docs/2026-08-26-rede-backend-design.md` (sections 3 "vagasRestantes é calculado", 5 "Vagas de evento (inscrição)", 7, 9 — "Fase 4 — Eventos & Inscrições") and `docs/2026-08-26-rede-backend-requisitos.md` (section 1.3 "Evento", section 1.4 "Inscricao", section 4 "Eventos", section 5 "Inscrições em eventos", section 8 "Resumo de autorização por papel").

## Global Constraints

- Target framework `net10.0` for every project (already set).
- Fixed dependency graph from Fase 1/2/3 (unchanged): `Domain` → nothing; `Application` → `Domain` only; `Infrastructure` → `Application` + `Domain`; `Api` → `Application` + `Infrastructure`. Repository **interfaces** (and the new `IUnitOfWork`/`ITransacao`) live in `RedeStore.Application.Common`; **implementations** live in `RedeStore.Infrastructure.Persistence`/`RedeStore.Infrastructure.Persistence.Repositories`. `Application` must never reference `Infrastructure` or EF Core types directly — this is why the transaction is wrapped behind `IUnitOfWork`/`ITransacao` instead of `InscricaoService` calling `DbContext.Database.BeginTransactionAsync` itself.
- `Evento.VagasRestantes` is **never a persisted column** — it is always `VagasTotais - Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada)`, computed in `EventoService`/`InscricaoService` mapping and included in every `EventoDto` response plus the dedicated `GET /eventos/:id/vagas-restantes` endpoint (per design doc section 3 and requisitos section 5).
- `Inscricao.Status` is a closed 2-value enum (`Confirmada`, `Cancelada`) that maps 1:1 to the lowercase wire strings `"confirmada"`/`"cancelada"` via `status.ToString().ToLowerInvariant()` — the exact same convention `Papel`/`CategoriaProduto` already use.
- **Vagas lock (design doc section 5, verbatim pattern to follow):** inside a transaction, first `SELECT 1 FROM "Eventos" WHERE "Id" = @eventoId FOR UPDATE` to serialize concurrent inscriptions on the same event, only then count confirmed inscriptions and decide between `criada`/`ja_inscrito`/`esgotado`, all before commit. This is implemented as `IEventoRepository.LockAndCountInscricoesConfirmadasAsync` (raw SQL isolated in `Infrastructure`, exact name from the design doc) so `InscricaoService` never knows it's a pessimistic lock under the hood — it just calls the method and branches on the result.
- The new `IUnitOfWork.IniciarTransacaoAsync` must be called **before** `LockAndCountInscricoesConfirmadasAsync` and the transaction committed only after the create-or-not decision is made — this is what makes the idempotency check (`ja_inscrito`) and the vagas check (`esgotado`) both race-safe, not just the vagas check alone.
- No mocking library. Unit tests needing a fake repository/unit-of-work use small hand-rolled in-memory classes implementing the same interfaces — not Moq/NSubstitute (same rule as Fase 2/3). Because `EventoService`'s delete-guard and `InscricaoService`'s vagas/idempotency logic both read the `Evento.Inscricoes` navigation, `FakeInscricaoRepository` in the unit tests must share the same in-memory `Evento` instances as `FakeEventoRepository` (constructor-injected) so that adding a fake inscription is visible to the fake lock-and-count method — mirroring how EF Core keeps a tracked entity's navigation consistent within one `DbContext` scope.
- `DELETE /eventos/:id` is blocked (`409`) when the event has at least one `Confirmada` inscription — the option the requirements doc (section 4) explicitly recommends, to avoid "Minhas inscrições" showing an orphaned `eventoId`. Events with zero or only-cancelled inscriptions can be deleted; the `Inscricoes` FK uses `DeleteBehavior.Cascade` (same convention as `Produto`/`Variacao`) so any leftover cancelled rows are removed along with the event once the service-level guard has already confirmed none of them are `Confirmada`.
- Write endpoints (`POST`/`PATCH`/`DELETE /eventos`, `GET /eventos/:id/inscricoes`) require the `"Admin"` authorization policy already registered in `Program.cs`; `POST /eventos/:id/inscricoes`, `GET /usuarios/me/inscricoes`, `PATCH /inscricoes/:id/cancelar` require `.RequireAuthorization()` (any authenticated role); `GET /eventos`, `GET /eventos/:id`, `GET /eventos/:id/vagas-restantes` stay public — per the authorization table in the requirements doc (section 8).
- `PATCH /inscricoes/:id/cancelar` is dono-OU-admin: extract `sub`/`IsInRole("admin")` from `ClaimsPrincipal` exactly like `UsuariosEndpoints.MapUsuariosEndpoints` already does, pass both into `InscricaoService.CancelarAsync`, which throws the existing `AcessoNegadoException` (Fase 2, `"ACESSO_NEGADO"`, 403) when neither condition holds — no new exception type needed for this check.
- Shared testing convention (same as Fase 2/3): integration tests share one Postgres container/database across the whole run via `ICollectionFixture`, so tests must not collide — use randomly-suffixed unique values (e.g. event titles with a `Guid` in them) wherever a test asserts on a specific row showing up in a list/search result.
- `valorPago` on `Inscricao` is a snapshot of `Evento.Preco` taken at the moment of inscription (never re-read from the event later) — same snapshot principle the design doc uses for `ItemPedido` (Fase 5).

---

## Task 1: Evento & Inscricao Persistence + Unit-of-Work

**Files:**
- Create: `src/RedeStore.Domain/Entities/StatusInscricao.cs`
- Create: `src/RedeStore.Domain/Entities/Evento.cs`
- Create: `src/RedeStore.Domain/Entities/Inscricao.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/EventoConfiguration.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/InscricaoConfiguration.cs`
- Modify: `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs`
- Create: `src/RedeStore.Application/Common/IEventoRepository.cs`
- Create: `src/RedeStore.Application/Common/IInscricaoRepository.cs`
- Create: `src/RedeStore.Application/Common/IUnitOfWork.cs`
- Create: `src/RedeStore.Application/Common/ITransacao.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/EventoRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/InscricaoRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/EfUnitOfWork.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Migration: generated under `src/RedeStore.Infrastructure/Persistence/Migrations/`
- Test: `tests/RedeStore.IntegrationTests/Persistence/EventoRepositoryTests.cs`
- Test: `tests/RedeStore.IntegrationTests/Persistence/InscricaoRepositoryTests.cs`

**Interfaces:**
- Consumes: `ApiFactory`/`IntegrationTestCollection` (Fase 2, unchanged).
- Produces: `RedeStore.Application.Common.IEventoRepository` — `Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct)`, `Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct)`, `Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct)`, `Task AdicionarAsync(Evento evento, CancellationToken ct)`, `Task AtualizarAsync(Evento evento, CancellationToken ct)`, `Task RemoverAsync(Evento evento, CancellationToken ct)`. `RedeStore.Application.Common.IInscricaoRepository` — `Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct)`, `Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct)`, `Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)`, `Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct)`, `Task AdicionarAsync(Inscricao inscricao, CancellationToken ct)`, `Task AtualizarAsync(Inscricao inscricao, CancellationToken ct)`. `RedeStore.Application.Common.IUnitOfWork` — `Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct)`. `RedeStore.Application.Common.ITransacao : IAsyncDisposable` — `Task ConfirmarAsync(CancellationToken ct)`. All four registered as `Scoped` in DI. Task 3 and Task 4 depend on these exact signatures.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Persistence/EventoRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class EventoRepositoryTests
{
    private readonly ApiFactory _factory;

    public EventoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static Evento CriarEventoDeTeste(string? titulo = null, DateTime? dataHora = null) => new()
    {
        Id = Guid.NewGuid(),
        Titulo = titulo ?? $"Culto Jovem {Guid.NewGuid()}",
        Descricao = "Encontro semanal dos jovens",
        DataHora = dataHora ?? DateTime.UtcNow.AddDays(7),
        Local = "Templo Sede",
        Preco = 0m,
        VagasTotais = 50,
        Foto = "https://exemplo.com/evento.jpg",
    };

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaEvento()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var evento = CriarEventoDeTeste();

        await repositorio.AdicionarAsync(evento, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(evento.Id, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(evento.Titulo, encontrado!.Titulo);
        Assert.Empty(encontrado.Inscricoes);
    }

    [Fact]
    public async Task BuscarPorIdAsync_ComIdInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();

        var encontrado = await repositorio.BuscarPorIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ListarAsync_ComApenasFuturosTrue_RetornaSoEventosFuturos()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var futuro = CriarEventoDeTeste(dataHora: DateTime.UtcNow.AddDays(7));
        var passado = CriarEventoDeTeste(dataHora: DateTime.UtcNow.AddDays(-7));
        await repositorio.AdicionarAsync(futuro, CancellationToken.None);
        await repositorio.AdicionarAsync(passado, CancellationToken.None);

        var resultado = await repositorio.ListarAsync(apenasFuturos: true, CancellationToken.None);

        Assert.Contains(resultado, e => e.Id == futuro.Id);
        Assert.DoesNotContain(resultado, e => e.Id == passado.Id);
    }

    [Fact]
    public async Task ListarAsync_ComApenasFuturosFalse_RetornaTodos()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var passado = CriarEventoDeTeste(dataHora: DateTime.UtcNow.AddDays(-7));
        await repositorio.AdicionarAsync(passado, CancellationToken.None);

        var resultado = await repositorio.ListarAsync(apenasFuturos: false, CancellationToken.None);

        Assert.Contains(resultado, e => e.Id == passado.Id);
    }

    [Fact]
    public async Task LockAndCountInscricoesConfirmadasAsync_ComEventoInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await using var transacao = await unitOfWork.IniciarTransacaoAsync(CancellationToken.None);
        var resultado = await eventoRepositorio.LockAndCountInscricoesConfirmadasAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task LockAndCountInscricoesConfirmadasAsync_ComInscricoesConfirmadasEAlgumaCancelada_ContaSoAsConfirmadas()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        await inscricaoRepositorio.AdicionarAsync(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = evento.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Confirmada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        }, CancellationToken.None);
        await inscricaoRepositorio.AdicionarAsync(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = evento.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Cancelada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        }, CancellationToken.None);

        await using var transacao = await unitOfWork.IniciarTransacaoAsync(CancellationToken.None);
        var resultado = await eventoRepositorio.LockAndCountInscricoesConfirmadasAsync(evento.Id, CancellationToken.None);
        await transacao.ConfirmarAsync(CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(evento.Id, resultado!.Value.Evento.Id);
        Assert.Equal(1, resultado.Value.VagasConfirmadas);
    }

    [Fact]
    public async Task RemoverAsync_ExcluiOEvento()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var evento = CriarEventoDeTeste();
        await repositorio.AdicionarAsync(evento, CancellationToken.None);

        await repositorio.RemoverAsync(evento, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(evento.Id, CancellationToken.None);

        Assert.Null(encontrado);
    }
}
```

Create `tests/RedeStore.IntegrationTests/Persistence/InscricaoRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class InscricaoRepositoryTests
{
    private readonly ApiFactory _factory;

    public InscricaoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static Evento CriarEventoDeTeste() => new()
    {
        Id = Guid.NewGuid(),
        Titulo = $"Retiro {Guid.NewGuid()}",
        Descricao = "Retiro anual",
        DataHora = DateTime.UtcNow.AddDays(30),
        Local = "Sítio da Rede",
        Preco = 50m,
        VagasTotais = 20,
        Foto = "https://exemplo.com/retiro.jpg",
    };

    private static Inscricao CriarInscricaoDeTeste(Guid eventoId, Guid? usuarioId = null, StatusInscricao status = StatusInscricao.Confirmada) => new()
    {
        Id = Guid.NewGuid(),
        EventoId = eventoId,
        UsuarioId = usuarioId ?? Guid.NewGuid(),
        Status = status,
        ValorPago = 50m,
        CriadoEm = DateTime.UtcNow,
    };

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaInscricao()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var inscricao = CriarInscricaoDeTeste(evento.Id);

        await inscricaoRepositorio.AdicionarAsync(inscricao, CancellationToken.None);
        var encontrada = await inscricaoRepositorio.BuscarPorIdAsync(inscricao.Id, CancellationToken.None);

        Assert.NotNull(encontrada);
        Assert.Equal(StatusInscricao.Confirmada, encontrada!.Status);
    }

    [Fact]
    public async Task BuscarConfirmadaPorEventoEUsuarioAsync_ComInscricaoCancelada_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuarioId = Guid.NewGuid();
        await inscricaoRepositorio.AdicionarAsync(CriarInscricaoDeTeste(evento.Id, usuarioId, StatusInscricao.Cancelada), CancellationToken.None);

        var encontrada = await inscricaoRepositorio.BuscarConfirmadaPorEventoEUsuarioAsync(evento.Id, usuarioId, CancellationToken.None);

        Assert.Null(encontrada);
    }

    [Fact]
    public async Task ListarPorUsuarioAsync_RetornaSoAsDoUsuario()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var usuarioId = Guid.NewGuid();
        var minha = CriarInscricaoDeTeste(evento.Id, usuarioId);
        var deOutro = CriarInscricaoDeTeste(evento.Id);
        await inscricaoRepositorio.AdicionarAsync(minha, CancellationToken.None);
        await inscricaoRepositorio.AdicionarAsync(deOutro, CancellationToken.None);

        var resultado = await inscricaoRepositorio.ListarPorUsuarioAsync(usuarioId, CancellationToken.None);

        Assert.Contains(resultado, i => i.Id == minha.Id);
        Assert.DoesNotContain(resultado, i => i.Id == deOutro.Id);
    }

    [Fact]
    public async Task AtualizarAsync_AlteraOStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var eventoRepositorio = scope.ServiceProvider.GetRequiredService<IEventoRepository>();
        var inscricaoRepositorio = scope.ServiceProvider.GetRequiredService<IInscricaoRepository>();
        var evento = CriarEventoDeTeste();
        await eventoRepositorio.AdicionarAsync(evento, CancellationToken.None);
        var inscricao = CriarInscricaoDeTeste(evento.Id);
        await inscricaoRepositorio.AdicionarAsync(inscricao, CancellationToken.None);

        inscricao.Status = StatusInscricao.Cancelada;
        await inscricaoRepositorio.AtualizarAsync(inscricao, CancellationToken.None);
        var recarregada = await inscricaoRepositorio.BuscarPorIdAsync(inscricao.Id, CancellationToken.None);

        Assert.Equal(StatusInscricao.Cancelada, recarregada!.Status);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~EventoRepositoryTests|FullyQualifiedName~InscricaoRepositoryTests"`
Expected: FAIL to compile — none of the entities/interfaces/repositories exist yet.

- [ ] **Step 3: Create the entities**

Create `src/RedeStore.Domain/Entities/StatusInscricao.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public enum StatusInscricao
{
    Confirmada,
    Cancelada,
}
```

Create `src/RedeStore.Domain/Entities/Evento.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Evento
{
    public Guid Id { get; set; }
    public required string Titulo { get; set; }
    public required string Descricao { get; set; }
    public DateTime DataHora { get; set; }
    public required string Local { get; set; }
    public decimal Preco { get; set; }
    public int VagasTotais { get; set; }
    public required string Foto { get; set; }
    public List<Inscricao> Inscricoes { get; set; } = [];
}
```

Create `src/RedeStore.Domain/Entities/Inscricao.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Inscricao
{
    public Guid Id { get; set; }
    public Guid EventoId { get; set; }
    public Guid UsuarioId { get; set; }
    public StatusInscricao Status { get; set; }
    public decimal ValorPago { get; set; }
    public DateTime CriadoEm { get; set; }
}
```

- [ ] **Step 4: Configure the entities and wire them into `RedeStoreDbContext`**

Create `src/RedeStore.Infrastructure/Persistence/Configurations/EventoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class EventoConfiguration : IEntityTypeConfiguration<Evento>
{
    public void Configure(EntityTypeBuilder<Evento> builder)
    {
        builder.ToTable("Eventos");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Titulo).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Descricao).IsRequired();
        builder.Property(e => e.Local).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Preco).HasPrecision(10, 2);
        builder.Property(e => e.Foto).IsRequired();
        builder.HasMany(e => e.Inscricoes).WithOne().HasForeignKey(i => i.EventoId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/RedeStore.Infrastructure/Persistence/Configurations/InscricaoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class InscricaoConfiguration : IEntityTypeConfiguration<Inscricao>
{
    public void Configure(EntityTypeBuilder<Inscricao> builder)
    {
        builder.ToTable("Inscricoes");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.ValorPago).HasPrecision(10, 2);
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
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Variacao> Variacoes => Set<Variacao>();
    public DbSet<Evento> Eventos => Set<Evento>();
    public DbSet<Inscricao> Inscricoes => Set<Inscricao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RedeStoreDbContext).Assembly);
    }
}
```

- [ ] **Step 5: Create the repository interfaces, the unit-of-work abstraction, and their implementations**

Create `src/RedeStore.Application/Common/IEventoRepository.cs`:

```csharp
using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IEventoRepository
{
    Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct);
    Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct);
    Task AdicionarAsync(Evento evento, CancellationToken ct);
    Task AtualizarAsync(Evento evento, CancellationToken ct);
    Task RemoverAsync(Evento evento, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Common/IInscricaoRepository.cs`:

```csharp
using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IInscricaoRepository
{
    Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct);
    Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct);
    Task AdicionarAsync(Inscricao inscricao, CancellationToken ct);
    Task AtualizarAsync(Inscricao inscricao, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Common/ITransacao.cs`:

```csharp
namespace RedeStore.Application.Common;

public interface ITransacao : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken ct);
}
```

Create `src/RedeStore.Application/Common/IUnitOfWork.cs`:

```csharp
namespace RedeStore.Application.Common;

public interface IUnitOfWork
{
    Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct);
}
```

Create `src/RedeStore.Infrastructure/Persistence/EfUnitOfWork.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Storage;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly RedeStoreDbContext _dbContext;

    public EfUnitOfWork(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct)
    {
        var transacaoEfCore = await _dbContext.Database.BeginTransactionAsync(ct);
        return new EfTransacao(transacaoEfCore);
    }

    private sealed class EfTransacao : ITransacao
    {
        private readonly IDbContextTransaction _transacao;

        public EfTransacao(IDbContextTransaction transacao)
        {
            _transacao = transacao;
        }

        public Task ConfirmarAsync(CancellationToken ct) => _transacao.CommitAsync(ct);

        public async ValueTask DisposeAsync() => await _transacao.DisposeAsync();
    }
}
```

`EfTransacao.DisposeAsync` rolling back an uncommitted transaction is standard `IDbContextTransaction` behavior — no explicit `RollbackAsync` call is needed in `InscricaoService`; letting an exception propagate out of the `await using` block is enough.

Create `src/RedeStore.Infrastructure/Persistence/Repositories/EventoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class EventoRepository : IEventoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public EventoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct)
    {
        var query = _dbContext.Eventos.Include(e => e.Inscricoes).AsQueryable();

        if (apenasFuturos)
        {
            var agora = DateTime.UtcNow;
            query = query.Where(e => e.DataHora >= agora);
        }

        return await query.ToListAsync(ct);
    }

    public Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Eventos.Include(e => e.Inscricoes).SingleOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct)
    {
        var linhaBloqueada = await _dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 FROM \"Eventos\" WHERE \"Id\" = {0} FOR UPDATE", eventoId)
            .ToListAsync(ct);

        if (linhaBloqueada.Count == 0)
        {
            return null;
        }

        var evento = await _dbContext.Eventos.AsNoTracking().SingleAsync(e => e.Id == eventoId, ct);
        var vagasConfirmadas = await _dbContext.Inscricoes
            .CountAsync(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada, ct);

        return (evento, vagasConfirmadas);
    }

    public async Task AdicionarAsync(Evento evento, CancellationToken ct)
    {
        _dbContext.Eventos.Add(evento);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Evento evento, CancellationToken ct)
    {
        _dbContext.Eventos.Update(evento);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(Evento evento, CancellationToken ct)
    {
        _dbContext.Eventos.Remove(evento);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

Create `src/RedeStore.Infrastructure/Persistence/Repositories/InscricaoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class InscricaoRepository : IInscricaoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public InscricaoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct) =>
        _dbContext.Inscricoes.SingleOrDefaultAsync(
            i => i.EventoId == eventoId && i.UsuarioId == usuarioId && i.Status == StatusInscricao.Confirmada, ct);

    public Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Inscricoes.SingleOrDefaultAsync(i => i.Id == id, ct);

    public Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        _dbContext.Inscricoes.Where(i => i.UsuarioId == usuarioId).OrderByDescending(i => i.CriadoEm).ToListAsync(ct);

    public Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct) =>
        _dbContext.Inscricoes.Where(i => i.EventoId == eventoId).OrderByDescending(i => i.CriadoEm).ToListAsync(ct);

    public async Task AdicionarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _dbContext.Inscricoes.Add(inscricao);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _dbContext.Inscricoes.Update(inscricao);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Register the repositories and unit-of-work in DI**

Open `src/RedeStore.Api/Program.cs` and add these lines right after the `IProdutoRepository` registration (keep everything else in the file unchanged):

```csharp
builder.Services.AddScoped<IEventoRepository, EventoRepository>();
builder.Services.AddScoped<IInscricaoRepository, InscricaoRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
```

No new `using` statements are needed — `RedeStore.Application.Common` and `RedeStore.Infrastructure.Persistence`/`RedeStore.Infrastructure.Persistence.Repositories` are already imported.

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add AdicionarEventosEInscricoes --project src/RedeStore.Infrastructure --startup-project src/RedeStore.Api --output-dir Persistence/Migrations
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~EventoRepositoryTests|FullyQualifiedName~InscricaoRepositoryTests"`
Expected: 10 passed. (`ApiFactory.InitializeAsync` applies the new `AdicionarEventosEInscricoes` migration automatically.)

- [ ] **Step 9: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 10: Commit**

```bash
git add src/RedeStore.Domain/Entities/StatusInscricao.cs src/RedeStore.Domain/Entities/Evento.cs src/RedeStore.Domain/Entities/Inscricao.cs src/RedeStore.Infrastructure/Persistence/Configurations/EventoConfiguration.cs src/RedeStore.Infrastructure/Persistence/Configurations/InscricaoConfiguration.cs src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs src/RedeStore.Application/Common/IEventoRepository.cs src/RedeStore.Application/Common/IInscricaoRepository.cs src/RedeStore.Application/Common/IUnitOfWork.cs src/RedeStore.Application/Common/ITransacao.cs src/RedeStore.Infrastructure/Persistence/Repositories/EventoRepository.cs src/RedeStore.Infrastructure/Persistence/Repositories/InscricaoRepository.cs src/RedeStore.Infrastructure/Persistence/EfUnitOfWork.cs src/RedeStore.Infrastructure/Persistence/Migrations/ src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Persistence/EventoRepositoryTests.cs tests/RedeStore.IntegrationTests/Persistence/InscricaoRepositoryTests.cs
git commit -m "Add Evento and Inscricao persistence with pessimistic-lock unit-of-work"
```

---

## Task 2: Evento DTOs and FluentValidation Validators

**Files:**
- Create: `src/RedeStore.Application/Eventos/Dtos/CriarEventoRequest.cs`
- Create: `src/RedeStore.Application/Eventos/Dtos/AtualizarEventoRequest.cs`
- Create: `src/RedeStore.Application/Eventos/Dtos/EventoDto.cs`
- Create: `src/RedeStore.Application/Eventos/Dtos/VagasRestantesDto.cs`
- Create: `src/RedeStore.Application/Eventos/Validators/CriarEventoRequestValidator.cs`
- Create: `src/RedeStore.Application/Eventos/Validators/AtualizarEventoRequestValidator.cs`
- Test: `tests/RedeStore.UnitTests/Eventos/Validators/CriarEventoRequestValidatorTests.cs`
- Test: `tests/RedeStore.UnitTests/Eventos/Validators/AtualizarEventoRequestValidatorTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 directly (DTOs are plain data shapes).
- Produces: `CriarEventoRequest(string Titulo, string Descricao, DateTime DataHora, string Local, decimal Preco, int VagasTotais, string Foto)`, `AtualizarEventoRequest(string? Titulo, string? Descricao, DateTime? DataHora, string? Local, decimal? Preco, int? VagasTotais, string? Foto)`, `EventoDto(Guid Id, string Titulo, string Descricao, DateTime DataHora, string Local, decimal Preco, int VagasTotais, int VagasRestantes, string Foto)`, `VagasRestantesDto(int VagasRestantes)`. Task 3 and Task 5 depend on these exact shapes; Task 5 wires `CriarEventoRequestValidator`/`AtualizarEventoRequestValidator` into `ValidationFilter<T>` (Fase 2).

- [ ] **Step 1: Write the failing validator tests**

Create `tests/RedeStore.UnitTests/Eventos/Validators/CriarEventoRequestValidatorTests.cs`:

```csharp
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Eventos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Eventos.Validators;

public class CriarEventoRequestValidatorTests
{
    private readonly CriarEventoRequestValidator _validator = new();

    private static CriarEventoRequest RequestValido() => new(
        Titulo: "Culto Jovem",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: 50,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public void Validate_ComRequestValido_NaoRetornaErros()
    {
        var resultado = _validator.Validate(RequestValido());

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComTituloVazio_RetornaErro()
    {
        var request = RequestValido() with { Titulo = "" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoNegativo_RetornaErro()
    {
        var request = RequestValido() with { Preco = -1m };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoZero_NaoRetornaErro()
    {
        var request = RequestValido() with { Preco = 0m };

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComVagasTotaisZero_RetornaErro()
    {
        var request = RequestValido() with { VagasTotais = 0 };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFotoVazia_RetornaErro()
    {
        var request = RequestValido() with { Foto = "" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
```

Create `tests/RedeStore.UnitTests/Eventos/Validators/AtualizarEventoRequestValidatorTests.cs`:

```csharp
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Eventos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Eventos.Validators;

public class AtualizarEventoRequestValidatorTests
{
    private readonly AtualizarEventoRequestValidator _validator = new();

    [Fact]
    public void Validate_ComTodosOsCamposNulos_NaoRetornaErros()
    {
        var request = new AtualizarEventoRequest(null, null, null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComTituloVazio_RetornaErro()
    {
        var request = new AtualizarEventoRequest("", null, null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoNegativo_RetornaErro()
    {
        var request = new AtualizarEventoRequest(null, null, null, null, -1m, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComVagasTotaisZero_RetornaErro()
    {
        var request = new AtualizarEventoRequest(null, null, null, null, null, 0, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~CriarEventoRequestValidatorTests|FullyQualifiedName~AtualizarEventoRequestValidatorTests"`
Expected: FAIL to compile — none of the DTOs/validators exist yet.

- [ ] **Step 3: Create the DTOs**

Create `src/RedeStore.Application/Eventos/Dtos/CriarEventoRequest.cs`:

```csharp
namespace RedeStore.Application.Eventos.Dtos;

public sealed record CriarEventoRequest(
    string Titulo,
    string Descricao,
    DateTime DataHora,
    string Local,
    decimal Preco,
    int VagasTotais,
    string Foto);
```

Create `src/RedeStore.Application/Eventos/Dtos/AtualizarEventoRequest.cs`:

```csharp
namespace RedeStore.Application.Eventos.Dtos;

public sealed record AtualizarEventoRequest(
    string? Titulo,
    string? Descricao,
    DateTime? DataHora,
    string? Local,
    decimal? Preco,
    int? VagasTotais,
    string? Foto);
```

Create `src/RedeStore.Application/Eventos/Dtos/EventoDto.cs`:

```csharp
namespace RedeStore.Application.Eventos.Dtos;

public sealed record EventoDto(
    Guid Id,
    string Titulo,
    string Descricao,
    DateTime DataHora,
    string Local,
    decimal Preco,
    int VagasTotais,
    int VagasRestantes,
    string Foto);
```

Create `src/RedeStore.Application/Eventos/Dtos/VagasRestantesDto.cs`:

```csharp
namespace RedeStore.Application.Eventos.Dtos;

public sealed record VagasRestantesDto(int VagasRestantes);
```

- [ ] **Step 4: Implement the validators**

Create `src/RedeStore.Application/Eventos/Validators/CriarEventoRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Application.Eventos.Validators;

public sealed class CriarEventoRequestValidator : AbstractValidator<CriarEventoRequest>
{
    public CriarEventoRequestValidator()
    {
        RuleFor(r => r.Titulo).NotEmpty();
        RuleFor(r => r.Descricao).NotEmpty();
        RuleFor(r => r.Local).NotEmpty();
        RuleFor(r => r.Foto).NotEmpty();
        RuleFor(r => r.Preco).GreaterThanOrEqualTo(0);
        RuleFor(r => r.VagasTotais).GreaterThanOrEqualTo(1);
    }
}
```

Create `src/RedeStore.Application/Eventos/Validators/AtualizarEventoRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Application.Eventos.Validators;

public sealed class AtualizarEventoRequestValidator : AbstractValidator<AtualizarEventoRequest>
{
    public AtualizarEventoRequestValidator()
    {
        RuleFor(r => r.Titulo).NotEmpty().When(r => r.Titulo is not null);
        RuleFor(r => r.Descricao).NotEmpty().When(r => r.Descricao is not null);
        RuleFor(r => r.Local).NotEmpty().When(r => r.Local is not null);
        RuleFor(r => r.Foto).NotEmpty().When(r => r.Foto is not null);
        RuleFor(r => r.Preco).GreaterThanOrEqualTo(0).When(r => r.Preco.HasValue);
        RuleFor(r => r.VagasTotais).GreaterThanOrEqualTo(1).When(r => r.VagasTotais.HasValue);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~CriarEventoRequestValidatorTests|FullyQualifiedName~AtualizarEventoRequestValidatorTests"`
Expected: 10 passed.

- [ ] **Step 6: Commit**

```bash
git add src/RedeStore.Application/Eventos/Dtos/ src/RedeStore.Application/Eventos/Validators/ tests/RedeStore.UnitTests/Eventos/Validators/
git commit -m "Add Evento DTOs and FluentValidation validators"
```

---

## Task 3: EventoService

**Files:**
- Create: `src/RedeStore.Domain/Exceptions/EventoNaoEncontradoException.cs`
- Create: `src/RedeStore.Domain/Exceptions/EventoComInscricoesConfirmadasException.cs`
- Create: `src/RedeStore.Application/Eventos/IEventoService.cs`
- Create: `src/RedeStore.Application/Eventos/EventoService.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Domain/EventoNaoEncontradoExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Domain/EventoComInscricoesConfirmadasExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Eventos/FakeEventoRepository.cs`
- Test: `tests/RedeStore.UnitTests/Eventos/EventoServiceTests.cs`

**Interfaces:**
- Consumes: `IEventoRepository` (Task 1), the DTOs from Task 2.
- Produces: `RedeStore.Application.Eventos.IEventoService` — `Task<List<EventoDto>> ListarAsync(bool apenasFuturos, CancellationToken ct)`, `Task<EventoDto> ObterPorIdAsync(Guid id, CancellationToken ct)`, `Task<VagasRestantesDto> ObterVagasRestantesAsync(Guid id, CancellationToken ct)`, `Task<EventoDto> CriarAsync(CriarEventoRequest request, CancellationToken ct)`, `Task<EventoDto> AtualizarAsync(Guid id, AtualizarEventoRequest request, CancellationToken ct)`, `Task RemoverAsync(Guid id, CancellationToken ct)`. `RedeStore.Domain.Exceptions.EventoNaoEncontradoException` (`"EVENTO_NAO_ENCONTRADO"`, 404), `RedeStore.Domain.Exceptions.EventoComInscricoesConfirmadasException` (`"EVENTO_COM_INSCRICOES_CONFIRMADAS"`, 409). `tests/RedeStore.UnitTests/Eventos/FakeEventoRepository.cs` exposes a public `EventosPorId` dictionary — Task 4's `FakeInscricaoRepository` depends on reusing this exact field so fake inscriptions become visible to the fake event's `Inscricoes` navigation, the same way EF Core keeps a tracked entity consistent within one `DbContext` scope. Task 5 maps all `IEventoService` methods to HTTP endpoints and relies on both new exceptions producing their status codes via the existing `GlobalExceptionHandler`.

- [ ] **Step 1: Write the failing exception tests**

Create `tests/RedeStore.UnitTests/Domain/EventoNaoEncontradoExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EventoNaoEncontradoExceptionTests
{
    [Fact]
    public void EventoNaoEncontradoException_TemCodigoEStatusCorretos()
    {
        var exception = new EventoNaoEncontradoException("evento não encontrado");

        Assert.Equal("EVENTO_NAO_ENCONTRADO", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
```

Create `tests/RedeStore.UnitTests/Domain/EventoComInscricoesConfirmadasExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EventoComInscricoesConfirmadasExceptionTests
{
    [Fact]
    public void EventoComInscricoesConfirmadasException_TemCodigoEStatusCorretos()
    {
        var exception = new EventoComInscricoesConfirmadasException("evento tem inscrições confirmadas");

        Assert.Equal("EVENTO_COM_INSCRICOES_CONFIRMADAS", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/RedeStore.UnitTests/Eventos/FakeEventoRepository.cs` (an in-memory test double, not a mock — Global Constraints forbid mocking libraries):

```csharp
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Eventos;

public sealed class FakeEventoRepository : IEventoRepository
{
    public Dictionary<Guid, Evento> EventosPorId { get; } = new();

    public Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct)
    {
        var query = EventosPorId.Values.AsEnumerable();

        if (apenasFuturos)
        {
            var agora = DateTime.UtcNow;
            query = query.Where(e => e.DataHora >= agora);
        }

        return Task.FromResult(query.ToList());
    }

    public Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(EventosPorId.GetValueOrDefault(id));

    public Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct)
    {
        if (!EventosPorId.TryGetValue(eventoId, out var evento))
        {
            return Task.FromResult<(Evento, int)?>(null);
        }

        var vagasConfirmadas = evento.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada);
        return Task.FromResult<(Evento, int)?>((evento, vagasConfirmadas));
    }

    public Task AdicionarAsync(Evento evento, CancellationToken ct)
    {
        EventosPorId[evento.Id] = evento;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Evento evento, CancellationToken ct)
    {
        EventosPorId[evento.Id] = evento;
        return Task.CompletedTask;
    }

    public Task RemoverAsync(Evento evento, CancellationToken ct)
    {
        EventosPorId.Remove(evento.Id);
        return Task.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Eventos/EventoServiceTests.cs`:

```csharp
using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Eventos;

public class EventoServiceTests
{
    private readonly FakeEventoRepository _repositorio = new();
    private readonly EventoService _sut;

    public EventoServiceTests()
    {
        _sut = new EventoService(_repositorio);
    }

    private static CriarEventoRequest RequestValido(string? titulo = null, int vagasTotais = 10) => new(
        Titulo: titulo ?? "Culto Jovem",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: vagasTotais,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public async Task CriarAsync_ComVagasTotaisDez_RetornaVagasRestantesDez()
    {
        var resposta = await _sut.CriarAsync(RequestValido(vagasTotais: 10), CancellationToken.None);

        Assert.Equal(10, resposta.VagasRestantes);
    }

    [Fact]
    public async Task ObterPorIdAsync_ComIdInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.ObterPorIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ObterPorIdAsync_ComInscricaoConfirmada_DescontaDasVagasRestantes()
    {
        var criado = await _sut.CriarAsync(RequestValido(vagasTotais: 10), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Confirmada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        var encontrado = await _sut.ObterPorIdAsync(criado.Id, CancellationToken.None);

        Assert.Equal(9, encontrado.VagasRestantes);
    }

    [Fact]
    public async Task ObterVagasRestantesAsync_IgnoraInscricoesCanceladas()
    {
        var criado = await _sut.CriarAsync(RequestValido(vagasTotais: 10), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Cancelada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        var resultado = await _sut.ObterVagasRestantesAsync(criado.Id, CancellationToken.None);

        Assert.Equal(10, resultado.VagasRestantes);
    }

    [Fact]
    public async Task AtualizarAsync_ComIdInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.AtualizarAsync(Guid.NewGuid(), new AtualizarEventoRequest(null, null, null, null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarAsync_ComApenasTitulo_MantemOsOutrosCampos()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        var atualizado = await _sut.AtualizarAsync(
            criado.Id,
            new AtualizarEventoRequest("Novo Título", null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal("Novo Título", atualizado.Titulo);
        Assert.Equal(criado.Local, atualizado.Local);
    }

    [Fact]
    public async Task RemoverAsync_ComIdInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.RemoverAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RemoverAsync_ComInscricaoConfirmada_LancaEventoComInscricoesConfirmadasException()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Confirmada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<EventoComInscricoesConfirmadasException>(() =>
            _sut.RemoverAsync(criado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RemoverAsync_ComApenasInscricaoCancelada_RemoveOEvento()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);
        _repositorio.EventosPorId[criado.Id].Inscricoes.Add(new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = criado.Id,
            UsuarioId = Guid.NewGuid(),
            Status = StatusInscricao.Cancelada,
            ValorPago = 0m,
            CriadoEm = DateTime.UtcNow,
        });

        await _sut.RemoverAsync(criado.Id, CancellationToken.None);

        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.ObterPorIdAsync(criado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListarAsync_ComApenasFuturosTrue_NaoRetornaEventosPassados()
    {
        await _sut.CriarAsync(RequestValido("Evento Futuro"), CancellationToken.None);
        var eventoPassadoId = Guid.NewGuid();
        _repositorio.EventosPorId[eventoPassadoId] = new Evento
        {
            Id = eventoPassadoId,
            Titulo = "Evento Passado",
            Descricao = "Já aconteceu",
            DataHora = DateTime.UtcNow.AddDays(-7),
            Local = "Templo Sede",
            Preco = 0m,
            VagasTotais = 10,
            Foto = "https://exemplo.com/evento.jpg",
        };

        var resultado = await _sut.ListarAsync(apenasFuturos: true, CancellationToken.None);

        Assert.DoesNotContain(resultado, e => e.Id == eventoPassadoId);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~EventoNaoEncontradoExceptionTests|FullyQualifiedName~EventoComInscricoesConfirmadasExceptionTests|FullyQualifiedName~EventoServiceTests"`
Expected: FAIL to compile — `EventoNaoEncontradoException`, `EventoComInscricoesConfirmadasException`, `IEventoService`, `EventoService` don't exist yet.

- [ ] **Step 4: Implement the exceptions**

Create `src/RedeStore.Domain/Exceptions/EventoNaoEncontradoException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class EventoNaoEncontradoException : DomainException
{
    public override string Codigo => "EVENTO_NAO_ENCONTRADO";
    public override int StatusCode => 404;

    public EventoNaoEncontradoException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/EventoComInscricoesConfirmadasException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class EventoComInscricoesConfirmadasException : DomainException
{
    public override string Codigo => "EVENTO_COM_INSCRICOES_CONFIRMADAS";
    public override int StatusCode => 409;

    public EventoComInscricoesConfirmadasException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 5: Implement `IEventoService` and `EventoService`**

Create `src/RedeStore.Application/Eventos/IEventoService.cs`:

```csharp
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Application.Eventos;

public interface IEventoService
{
    Task<List<EventoDto>> ListarAsync(bool apenasFuturos, CancellationToken ct);
    Task<EventoDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<VagasRestantesDto> ObterVagasRestantesAsync(Guid id, CancellationToken ct);
    Task<EventoDto> CriarAsync(CriarEventoRequest request, CancellationToken ct);
    Task<EventoDto> AtualizarAsync(Guid id, AtualizarEventoRequest request, CancellationToken ct);
    Task RemoverAsync(Guid id, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Eventos/EventoService.cs`:

```csharp
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Eventos;

public sealed class EventoService : IEventoService
{
    private readonly IEventoRepository _eventoRepository;

    public EventoService(IEventoRepository eventoRepository)
    {
        _eventoRepository = eventoRepository;
    }

    public async Task<List<EventoDto>> ListarAsync(bool apenasFuturos, CancellationToken ct)
    {
        var eventos = await _eventoRepository.ListarAsync(apenasFuturos, ct);
        return eventos.Select(MapearParaDto).ToList();
    }

    public async Task<EventoDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");
        return MapearParaDto(evento);
    }

    public async Task<VagasRestantesDto> ObterVagasRestantesAsync(Guid id, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");
        return new VagasRestantesDto(CalcularVagasRestantes(evento));
    }

    public async Task<EventoDto> CriarAsync(CriarEventoRequest request, CancellationToken ct)
    {
        var evento = new Evento
        {
            Id = Guid.NewGuid(),
            Titulo = request.Titulo,
            Descricao = request.Descricao,
            DataHora = request.DataHora,
            Local = request.Local,
            Preco = request.Preco,
            VagasTotais = request.VagasTotais,
            Foto = request.Foto,
        };

        await _eventoRepository.AdicionarAsync(evento, ct);
        return MapearParaDto(evento);
    }

    public async Task<EventoDto> AtualizarAsync(Guid id, AtualizarEventoRequest request, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");

        if (request.Titulo is not null)
        {
            evento.Titulo = request.Titulo;
        }

        if (request.Descricao is not null)
        {
            evento.Descricao = request.Descricao;
        }

        if (request.DataHora is not null)
        {
            evento.DataHora = request.DataHora.Value;
        }

        if (request.Local is not null)
        {
            evento.Local = request.Local;
        }

        if (request.Preco is not null)
        {
            evento.Preco = request.Preco.Value;
        }

        if (request.VagasTotais is not null)
        {
            evento.VagasTotais = request.VagasTotais.Value;
        }

        if (request.Foto is not null)
        {
            evento.Foto = request.Foto;
        }

        await _eventoRepository.AtualizarAsync(evento, ct);
        return MapearParaDto(evento);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");

        if (evento.Inscricoes.Any(i => i.Status == StatusInscricao.Confirmada))
        {
            throw new EventoComInscricoesConfirmadasException(
                $"Evento '{id}' tem inscrições confirmadas e não pode ser removido.");
        }

        await _eventoRepository.RemoverAsync(evento, ct);
    }

    private static int CalcularVagasRestantes(Evento evento) =>
        evento.VagasTotais - evento.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada);

    private static EventoDto MapearParaDto(Evento evento) => new(
        evento.Id,
        evento.Titulo,
        evento.Descricao,
        evento.DataHora,
        evento.Local,
        evento.Preco,
        evento.VagasTotais,
        CalcularVagasRestantes(evento),
        evento.Foto);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~EventoNaoEncontradoExceptionTests|FullyQualifiedName~EventoComInscricoesConfirmadasExceptionTests|FullyQualifiedName~EventoServiceTests"`
Expected: 13 passed.

- [ ] **Step 7: Register the service and validators in DI**

Open `src/RedeStore.Api/Program.cs`. Add these `using` statements:

```csharp
using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Eventos.Validators;
```

Add these registrations right after the `IValidator<AtualizarProdutoRequest>` registration from Fase 3:

```csharp
builder.Services.AddScoped<IEventoService, EventoService>();
builder.Services.AddSingleton<IValidator<CriarEventoRequest>, CriarEventoRequestValidator>();
builder.Services.AddSingleton<IValidator<AtualizarEventoRequest>, AtualizarEventoRequestValidator>();
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/RedeStore.Domain/Exceptions/EventoNaoEncontradoException.cs src/RedeStore.Domain/Exceptions/EventoComInscricoesConfirmadasException.cs src/RedeStore.Application/Eventos/IEventoService.cs src/RedeStore.Application/Eventos/EventoService.cs src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Domain/EventoNaoEncontradoExceptionTests.cs tests/RedeStore.UnitTests/Domain/EventoComInscricoesConfirmadasExceptionTests.cs tests/RedeStore.UnitTests/Eventos/FakeEventoRepository.cs tests/RedeStore.UnitTests/Eventos/EventoServiceTests.cs
git commit -m "Add EventoService covering catalog CRUD and computed vagasRestantes"
```

---

## Task 4: InscricaoService

**Files:**
- Create: `src/RedeStore.Domain/Exceptions/InscricaoNaoEncontradaException.cs`
- Create: `src/RedeStore.Application/Inscricoes/Dtos/InscricaoDto.cs`
- Create: `src/RedeStore.Application/Inscricoes/Dtos/ResultadoInscricaoDto.cs`
- Create: `src/RedeStore.Application/Inscricoes/IInscricaoService.cs`
- Create: `src/RedeStore.Application/Inscricoes/InscricaoService.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Domain/InscricaoNaoEncontradaExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Inscricoes/FakeInscricaoRepository.cs`
- Test: `tests/RedeStore.UnitTests/Inscricoes/FakeUnitOfWork.cs`
- Test: `tests/RedeStore.UnitTests/Inscricoes/InscricaoServiceTests.cs`

**Interfaces:**
- Consumes: `IEventoRepository`, `IInscricaoRepository`, `IUnitOfWork` (Task 1), `FakeEventoRepository` (Task 3, reused as-is for the shared in-memory `Evento` instances).
- Produces: `RedeStore.Application.Inscricoes.IInscricaoService` — `Task<ResultadoInscricaoDto> InscreverAsync(Guid eventoId, Guid usuarioId, CancellationToken ct)`, `Task<List<InscricaoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)`, `Task<List<InscricaoDto>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct)`, `Task<InscricaoDto> CancelarAsync(Guid inscricaoId, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct)`. `InscricaoDto(Guid Id, Guid EventoId, Guid UsuarioId, string Status, decimal ValorPago, DateTime CriadoEm)` — `Status` is the lowercase string, never the enum. `ResultadoInscricaoDto(string Resultado, InscricaoDto? Inscricao)` — `Resultado` is one of `"criada"`/`"ja_inscrito"`/`"esgotado"`; `Inscricao` is `null` only when `Resultado == "esgotado"`. `RedeStore.Domain.Exceptions.InscricaoNaoEncontradaException` (`"INSCRICAO_NAO_ENCONTRADA"`, 404); `CancelarAsync` reuses the existing `RedeStore.Domain.Exceptions.AcessoNegadoException` (Fase 2) when the caller is neither the inscription's owner nor an admin. Task 5 maps all `IInscricaoService` methods to HTTP endpoints.

- [ ] **Step 1: Write the failing exception test**

Create `tests/RedeStore.UnitTests/Domain/InscricaoNaoEncontradaExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class InscricaoNaoEncontradaExceptionTests
{
    [Fact]
    public void InscricaoNaoEncontradaException_TemCodigoEStatusCorretos()
    {
        var exception = new InscricaoNaoEncontradaException("inscrição não encontrada");

        Assert.Equal("INSCRICAO_NAO_ENCONTRADA", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/RedeStore.UnitTests/Inscricoes/FakeInscricaoRepository.cs` — it shares `FakeEventoRepository.EventosPorId` so that adding an inscription updates the same `Evento.Inscricoes` list the lock-and-count fake reads from, mirroring how EF Core keeps a tracked entity's navigation in sync within one `DbContext` scope:

```csharp
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.UnitTests.Eventos;

namespace RedeStore.UnitTests.Inscricoes;

public sealed class FakeInscricaoRepository : IInscricaoRepository
{
    private readonly Dictionary<Guid, Evento> _eventosPorId;
    private readonly Dictionary<Guid, Inscricao> _inscricoesPorId = new();

    public FakeInscricaoRepository(FakeEventoRepository eventoRepository)
    {
        _eventosPorId = eventoRepository.EventosPorId;
    }

    public Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.Values.SingleOrDefault(
            i => i.EventoId == eventoId && i.UsuarioId == usuarioId && i.Status == StatusInscricao.Confirmada));

    public Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.GetValueOrDefault(id));

    public Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.Values.Where(i => i.UsuarioId == usuarioId).ToList());

    public Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.Values.Where(i => i.EventoId == eventoId).ToList());

    public Task AdicionarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _inscricoesPorId[inscricao.Id] = inscricao;
        if (_eventosPorId.TryGetValue(inscricao.EventoId, out var evento))
        {
            evento.Inscricoes.Add(inscricao);
        }
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _inscricoesPorId[inscricao.Id] = inscricao;
        return Task.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Inscricoes/FakeUnitOfWork.cs`:

```csharp
using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Inscricoes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct) =>
        Task.FromResult<ITransacao>(new FakeTransacao());

    private sealed class FakeTransacao : ITransacao
    {
        public Task ConfirmarAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Inscricoes/InscricaoServiceTests.cs`:

```csharp
using RedeStore.Application.Inscricoes;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;
using RedeStore.UnitTests.Eventos;
using Xunit;

namespace RedeStore.UnitTests.Inscricoes;

public class InscricaoServiceTests
{
    private readonly FakeEventoRepository _eventoRepositorio = new();
    private readonly FakeInscricaoRepository _inscricaoRepositorio;
    private readonly InscricaoService _sut;

    public InscricaoServiceTests()
    {
        _inscricaoRepositorio = new FakeInscricaoRepository(_eventoRepositorio);
        _sut = new InscricaoService(_eventoRepositorio, _inscricaoRepositorio, new FakeUnitOfWork());
    }

    private Guid CriarEvento(int vagasTotais, decimal preco = 25m)
    {
        var id = Guid.NewGuid();
        _eventoRepositorio.EventosPorId[id] = new Evento
        {
            Id = id,
            Titulo = "Retiro",
            Descricao = "Retiro anual",
            DataHora = DateTime.UtcNow.AddDays(30),
            Local = "Sítio da Rede",
            Preco = preco,
            VagasTotais = vagasTotais,
            Foto = "https://exemplo.com/retiro.jpg",
        };
        return id;
    }

    [Fact]
    public async Task InscreverAsync_ComVagaDisponivel_RetornaCriadaComValorPagoIgualAoPrecoDoEvento()
    {
        var eventoId = CriarEvento(vagasTotais: 10, preco: 25m);
        var usuarioId = Guid.NewGuid();

        var resultado = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        Assert.Equal("criada", resultado.Resultado);
        Assert.NotNull(resultado.Inscricao);
        Assert.Equal(25m, resultado.Inscricao!.ValorPago);
        Assert.Equal("confirmada", resultado.Inscricao.Status);
    }

    [Fact]
    public async Task InscreverAsync_ComEventoInexistente_LancaEventoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<EventoNaoEncontradoException>(() =>
            _sut.InscreverAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task InscreverAsync_MesmoUsuarioDuasVezes_RetornaJaInscritoNaSegundaVez()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var usuarioId = Guid.NewGuid();
        await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        var segunda = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        Assert.Equal("ja_inscrito", segunda.Resultado);
        var todasAsInscricoes = await _sut.ListarPorUsuarioAsync(usuarioId, CancellationToken.None);
        Assert.Single(todasAsInscricoes);
    }

    [Fact]
    public async Task InscreverAsync_QuandoVagasEsgotadas_RetornaEsgotadoSemCriarRegistro()
    {
        var eventoId = CriarEvento(vagasTotais: 1);
        await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        var resultado = await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("esgotado", resultado.Resultado);
        Assert.Null(resultado.Inscricao);
    }

    [Fact]
    public async Task CancelarAsync_ComIdInexistente_LancaInscricaoNaoEncontradaException()
    {
        await Assert.ThrowsAsync<InscricaoNaoEncontradaException>(() =>
            _sut.CancelarAsync(Guid.NewGuid(), Guid.NewGuid(), ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task CancelarAsync_PeloProprioDono_MarcaComoCancelada()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var usuarioId = Guid.NewGuid();
        var criada = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        var cancelada = await _sut.CancelarAsync(criada.Inscricao!.Id, usuarioId, ehAdmin: false, CancellationToken.None);

        Assert.Equal("cancelada", cancelada.Status);
    }

    [Fact]
    public async Task CancelarAsync_PeloAdmin_MarcaComoCancelada()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var criada = await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        var cancelada = await _sut.CancelarAsync(criada.Inscricao!.Id, Guid.NewGuid(), ehAdmin: true, CancellationToken.None);

        Assert.Equal("cancelada", cancelada.Status);
    }

    [Fact]
    public async Task CancelarAsync_PorUsuarioNaoDonoNaoAdmin_LancaAcessoNegadoException()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var criada = await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);

        await Assert.ThrowsAsync<AcessoNegadoException>(() =>
            _sut.CancelarAsync(criada.Inscricao!.Id, Guid.NewGuid(), ehAdmin: false, CancellationToken.None));
    }

    [Fact]
    public async Task InscreverAsync_AposCancelamento_PermiteNovaInscricaoDoMesmoUsuario()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        var usuarioId = Guid.NewGuid();
        var primeira = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);
        await _sut.CancelarAsync(primeira.Inscricao!.Id, usuarioId, ehAdmin: false, CancellationToken.None);

        var segunda = await _sut.InscreverAsync(eventoId, usuarioId, CancellationToken.None);

        Assert.Equal("criada", segunda.Resultado);
    }

    [Fact]
    public async Task ListarPorEventoAsync_RetornaAsInscricoesDaquelEvento()
    {
        var eventoId = CriarEvento(vagasTotais: 10);
        await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);
        await _sut.InscreverAsync(eventoId, Guid.NewGuid(), CancellationToken.None);
        var outroEventoId = CriarEvento(vagasTotais: 10);
        await _sut.InscreverAsync(outroEventoId, Guid.NewGuid(), CancellationToken.None);

        var resultado = await _sut.ListarPorEventoAsync(eventoId, CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, i => Assert.Equal(eventoId, i.EventoId));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~InscricaoNaoEncontradaExceptionTests|FullyQualifiedName~InscricaoServiceTests"`
Expected: FAIL to compile — `InscricaoNaoEncontradaException`, `IInscricaoService`, `InscricaoService` don't exist yet.

- [ ] **Step 4: Implement the exception and the DTOs**

Create `src/RedeStore.Domain/Exceptions/InscricaoNaoEncontradaException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class InscricaoNaoEncontradaException : DomainException
{
    public override string Codigo => "INSCRICAO_NAO_ENCONTRADA";
    public override int StatusCode => 404;

    public InscricaoNaoEncontradaException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Application/Inscricoes/Dtos/InscricaoDto.cs`:

```csharp
namespace RedeStore.Application.Inscricoes.Dtos;

public sealed record InscricaoDto(
    Guid Id,
    Guid EventoId,
    Guid UsuarioId,
    string Status,
    decimal ValorPago,
    DateTime CriadoEm);
```

Create `src/RedeStore.Application/Inscricoes/Dtos/ResultadoInscricaoDto.cs`:

```csharp
namespace RedeStore.Application.Inscricoes.Dtos;

public sealed record ResultadoInscricaoDto(string Resultado, InscricaoDto? Inscricao);
```

- [ ] **Step 5: Implement `IInscricaoService` and `InscricaoService`**

Create `src/RedeStore.Application/Inscricoes/IInscricaoService.cs`:

```csharp
using RedeStore.Application.Inscricoes.Dtos;

namespace RedeStore.Application.Inscricoes;

public interface IInscricaoService
{
    Task<ResultadoInscricaoDto> InscreverAsync(Guid eventoId, Guid usuarioId, CancellationToken ct);
    Task<List<InscricaoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<List<InscricaoDto>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct);
    Task<InscricaoDto> CancelarAsync(Guid inscricaoId, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Inscricoes/InscricaoService.cs`:

```csharp
using RedeStore.Application.Common;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Inscricoes;

public sealed class InscricaoService : IInscricaoService
{
    private readonly IEventoRepository _eventoRepository;
    private readonly IInscricaoRepository _inscricaoRepository;
    private readonly IUnitOfWork _unitOfWork;

    public InscricaoService(IEventoRepository eventoRepository, IInscricaoRepository inscricaoRepository, IUnitOfWork unitOfWork)
    {
        _eventoRepository = eventoRepository;
        _inscricaoRepository = inscricaoRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ResultadoInscricaoDto> InscreverAsync(Guid eventoId, Guid usuarioId, CancellationToken ct)
    {
        await using var transacao = await _unitOfWork.IniciarTransacaoAsync(ct);

        var resultadoLock = await _eventoRepository.LockAndCountInscricoesConfirmadasAsync(eventoId, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{eventoId}' não encontrado.");

        var existente = await _inscricaoRepository.BuscarConfirmadaPorEventoEUsuarioAsync(eventoId, usuarioId, ct);
        if (existente is not null)
        {
            await transacao.ConfirmarAsync(ct);
            return new ResultadoInscricaoDto("ja_inscrito", MapearParaDto(existente));
        }

        if (resultadoLock.Value.VagasConfirmadas >= resultadoLock.Value.Evento.VagasTotais)
        {
            await transacao.ConfirmarAsync(ct);
            return new ResultadoInscricaoDto("esgotado", null);
        }

        var inscricao = new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = eventoId,
            UsuarioId = usuarioId,
            Status = StatusInscricao.Confirmada,
            ValorPago = resultadoLock.Value.Evento.Preco,
            CriadoEm = DateTime.UtcNow,
        };
        await _inscricaoRepository.AdicionarAsync(inscricao, ct);
        await transacao.ConfirmarAsync(ct);

        return new ResultadoInscricaoDto("criada", MapearParaDto(inscricao));
    }

    public async Task<List<InscricaoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)
    {
        var inscricoes = await _inscricaoRepository.ListarPorUsuarioAsync(usuarioId, ct);
        return inscricoes.Select(MapearParaDto).ToList();
    }

    public async Task<List<InscricaoDto>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct)
    {
        var inscricoes = await _inscricaoRepository.ListarPorEventoAsync(eventoId, ct);
        return inscricoes.Select(MapearParaDto).ToList();
    }

    public async Task<InscricaoDto> CancelarAsync(Guid inscricaoId, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct)
    {
        var inscricao = await _inscricaoRepository.BuscarPorIdAsync(inscricaoId, ct)
            ?? throw new InscricaoNaoEncontradaException($"Inscrição '{inscricaoId}' não encontrada.");

        if (inscricao.UsuarioId != idUsuarioLogado && !ehAdmin)
        {
            throw new AcessoNegadoException("Você não tem permissão para cancelar esta inscrição.");
        }

        inscricao.Status = StatusInscricao.Cancelada;
        await _inscricaoRepository.AtualizarAsync(inscricao, ct);
        return MapearParaDto(inscricao);
    }

    private static InscricaoDto MapearParaDto(Inscricao inscricao) => new(
        inscricao.Id,
        inscricao.EventoId,
        inscricao.UsuarioId,
        inscricao.Status.ToString().ToLowerInvariant(),
        inscricao.ValorPago,
        inscricao.CriadoEm);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~InscricaoNaoEncontradaExceptionTests|FullyQualifiedName~InscricaoServiceTests"`
Expected: 10 passed.

- [ ] **Step 7: Register the service in DI**

Open `src/RedeStore.Api/Program.cs`. Add this `using` statement:

```csharp
using RedeStore.Application.Inscricoes;
```

Add this registration right after the `IEventoService` registration from this phase:

```csharp
builder.Services.AddScoped<IInscricaoService, InscricaoService>();
```

No validators are needed for `IInscricaoService` — its endpoints take no request body, only route parameters and the authenticated user's claims.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/RedeStore.Domain/Exceptions/InscricaoNaoEncontradaException.cs src/RedeStore.Application/Inscricoes/ src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Domain/InscricaoNaoEncontradaExceptionTests.cs tests/RedeStore.UnitTests/Inscricoes/
git commit -m "Add InscricaoService with pessimistic-lock vagas control and 3-outcome inscription flow"
```

---

## Task 5: Map the 10 Endpoints

**Files:**
- Create: `src/RedeStore.Api/Endpoints/EventosEndpoints.cs`
- Create: `src/RedeStore.Api/Endpoints/InscricoesEndpoints.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.IntegrationTests/Eventos/EventosEndpointsSmokeTests.cs`
- Test: `tests/RedeStore.IntegrationTests/Inscricoes/InscricoesEndpointsSmokeTests.cs`

**Interfaces:**
- Consumes: `IEventoService`, `IInscricaoService` (Tasks 3-4), `ValidationFilter<T>` (Fase 2), the `ClaimsPrincipal` self-or-admin extraction pattern already used by `UsuariosEndpoints.MapUsuariosEndpoints` (Fase 2).
- Produces: the 6 `/eventos*` routes and 4 inscrição routes wired into the running app. Task 6 exercises all of them end-to-end.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Eventos/EventosEndpointsSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Eventos;

[Collection(IntegrationTestCollection.Name)]
public class EventosEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public EventosEndpointsSmokeTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> ObterTokenAdminAsync()
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
        return login!.Token;
    }

    private static CriarEventoRequest RequestValido() => new(
        Titulo: $"Culto {Guid.NewGuid()}",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: 50,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public async Task PostEventos_SemToken_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/eventos", RequestValido());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostEventos_ComTokenDeAdmin_Retorna200EDepoisApareceNoDetalhe()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido());
        Assert.Equal(HttpStatusCode.OK, criarResponse.StatusCode);
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal(50, criado!.VagasRestantes);

        var detalheResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.OK, detalheResponse.StatusCode);
    }

    [Fact]
    public async Task PostEventos_ComVagasTotaisZero_Retorna400()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = RequestValido() with { VagasTotais = 0 };

        var response = await clienteAdmin.PostAsJsonAsync("/eventos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetEventoPorId_ComIdInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/eventos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetVagasRestantes_EPublico_RetornaVagasTotaisParaEventoSemInscricoes()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido());
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();

        var response = await _client.GetAsync($"/eventos/{criado!.Id}/vagas-restantes");

        response.EnsureSuccessStatusCode();
        var vagas = await response.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(50, vagas!.VagasRestantes);
    }
}
```

Create `tests/RedeStore.IntegrationTests/Inscricoes/InscricoesEndpointsSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using Xunit;

namespace RedeStore.IntegrationTests.Inscricoes;

[Collection(IntegrationTestCollection.Name)]
public class InscricoesEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public InscricoesEndpointsSmokeTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostInscricoes_SemToken_Retorna401()
    {
        var response = await _client.PostAsync($"/eventos/{Guid.NewGuid()}/inscricoes", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMinhasInscricoes_SemToken_Retorna401()
    {
        var response = await _client.GetAsync("/usuarios/me/inscricoes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostInscricoes_ComEventoInexistente_Retorna404()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        using var cliente = _factory.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var response = await cliente.PostAsync($"/eventos/{Guid.NewGuid()}/inscricoes", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~EventosEndpointsSmokeTests|FullyQualifiedName~InscricoesEndpointsSmokeTests"`
Expected: FAIL — routes return 404 (not mapped) instead of the expected status codes.

- [ ] **Step 3: Implement the endpoint groups**

Create `src/RedeStore.Api/Endpoints/EventosEndpoints.cs`:

```csharp
using RedeStore.Api.Filters;
using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class EventosEndpoints
{
    public static void MapEventosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/eventos");

        grupo.MapGet("/", async (bool? apenasFuturos, IEventoService eventoService, CancellationToken ct) =>
        {
            var eventos = await eventoService.ListarAsync(apenasFuturos ?? false, ct);
            return Results.Ok(eventos);
        });

        grupo.MapGet("/{id:guid}", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.ObterPorIdAsync(id, ct);
            return Results.Ok(evento);
        });

        grupo.MapGet("/{id:guid}/vagas-restantes", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            var vagas = await eventoService.ObterVagasRestantesAsync(id, ct);
            return Results.Ok(vagas);
        });

        grupo.MapPost("/", async (CriarEventoRequest request, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.CriarAsync(request, ct);
            return Results.Ok(evento);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<CriarEventoRequest>>();

        grupo.MapPatch("/{id:guid}", async (Guid id, AtualizarEventoRequest request, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.AtualizarAsync(id, request, ct);
            return Results.Ok(evento);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<AtualizarEventoRequest>>();

        grupo.MapDelete("/{id:guid}", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            await eventoService.RemoverAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }
}
```

Create `src/RedeStore.Api/Endpoints/InscricoesEndpoints.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Application.Inscricoes;

namespace RedeStore.Api.Endpoints;

public static class InscricoesEndpoints
{
    public static void MapInscricoesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/eventos/{eventoId:guid}/inscricoes", async (Guid eventoId, ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var resultado = await inscricaoService.InscreverAsync(eventoId, idUsuarioLogado, ct);
            return Results.Ok(resultado);
        }).RequireAuthorization();

        app.MapGet("/usuarios/me/inscricoes", async (ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var inscricoes = await inscricaoService.ListarPorUsuarioAsync(idUsuarioLogado, ct);
            return Results.Ok(inscricoes);
        }).RequireAuthorization();

        app.MapGet("/eventos/{eventoId:guid}/inscricoes", async (Guid eventoId, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var inscricoes = await inscricaoService.ListarPorEventoAsync(eventoId, ct);
            return Results.Ok(inscricoes);
        }).RequireAuthorization("Admin");

        app.MapPatch("/inscricoes/{id:guid}/cancelar", async (Guid id, ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var ehAdmin = user.IsInRole("admin");
            var inscricao = await inscricaoService.CancelarAsync(id, idUsuarioLogado, ehAdmin, ct);
            return Results.Ok(inscricao);
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 4: Map the endpoint groups in `Program.cs`**

Open `src/RedeStore.Api/Program.cs` and change:

```csharp
app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapProdutosEndpoints();
```

to:

```csharp
app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapProdutosEndpoints();
app.MapEventosEndpoints();
app.MapInscricoesEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~EventosEndpointsSmokeTests|FullyQualifiedName~InscricoesEndpointsSmokeTests"`
Expected: 8 passed.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/RedeStore.Api/Endpoints/EventosEndpoints.cs src/RedeStore.Api/Endpoints/InscricoesEndpoints.cs src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Eventos/ tests/RedeStore.IntegrationTests/Inscricoes/
git commit -m "Map the 10 eventos/inscricoes endpoints"
```

---

## Task 6: End-to-End Tests Covering the Fase 4 Definition of Done

**Files:**
- Create: `tests/RedeStore.IntegrationTests/Eventos/EventoFlowEndToEndTests.cs`
- Create: `tests/RedeStore.IntegrationTests/Inscricoes/InscricaoFlowEndToEndTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-5. No new production interfaces are produced by this task — it is the closing verification pass for the phase, matching the design doc's own required coverage (section 7: "Duas inscrições 'simultâneas' ... na última vaga de um evento → só uma vira confirmada").

- [ ] **Step 1: Write the end-to-end tests**

Create `tests/RedeStore.IntegrationTests/Eventos/EventoFlowEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Eventos;

[Collection(IntegrationTestCollection.Name)]
public class EventoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public EventoFlowEndToEndTests(ApiFactory factory)
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

    private static CriarEventoRequest RequestValido(int vagasTotais = 50) => new(
        Titulo: $"Culto {Guid.NewGuid()}",
        Descricao: "Encontro semanal",
        DataHora: DateTime.UtcNow.AddDays(7),
        Local: "Templo Sede",
        Preco: 0m,
        VagasTotais: vagasTotais,
        Foto: "https://exemplo.com/evento.jpg");

    [Fact]
    public async Task FluxoCompleto_CriarListarDetalharAtualizarDeletar_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var tituloUnico = $"Culto {Guid.NewGuid()}";

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido() with { Titulo = tituloUnico });
        criarResponse.EnsureSuccessStatusCode();
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal(50, criado!.VagasRestantes);

        var listaResponse = await _client.GetAsync("/eventos");
        listaResponse.EnsureSuccessStatusCode();
        var lista = await listaResponse.Content.ReadFromJsonAsync<List<EventoDto>>();
        Assert.Contains(lista!, e => e.Id == criado.Id);

        var detalheResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        detalheResponse.EnsureSuccessStatusCode();

        var atualizarResponse = await clienteAdmin.PatchAsJsonAsync($"/eventos/{criado.Id}",
            new AtualizarEventoRequest("Novo Título", null, null, null, null, null, null));
        atualizarResponse.EnsureSuccessStatusCode();
        var atualizado = await atualizarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal("Novo Título", atualizado!.Titulo);

        var detalheAposAtualizarResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        var detalheAposAtualizar = await detalheAposAtualizarResponse.Content.ReadFromJsonAsync<EventoDto>();
        Assert.Equal("Novo Título", detalheAposAtualizar!.Titulo);

        var deletarResponse = await clienteAdmin.DeleteAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletarResponse.StatusCode);

        var depoisDeDeletarResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NotFound, depoisDeDeletarResponse.StatusCode);
    }

    [Fact]
    public async Task PatchEventos_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        using var clienteNaoAdmin = _factory.CreateClient();
        clienteNaoAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);

        var response = await clienteNaoAdmin.PatchAsJsonAsync($"/eventos/{Guid.NewGuid()}",
            new AtualizarEventoRequest("Novo Título", null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvento_ComInscricaoConfirmada_Retorna409EEventoContinuaAcessivel()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", RequestValido());
        var criado = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();

        var email = $"{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/auth/cadastro", new CadastroRequest("Jovem", email, "senha12345"));
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new LoginRequest(email, "senha12345"));
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        using var clienteJovem = _factory.CreateClient();
        clienteJovem.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        await clienteJovem.PostAsync($"/eventos/{criado!.Id}/inscricoes", content: null);

        var deletarResponse = await clienteAdmin.DeleteAsync($"/eventos/{criado.Id}");

        Assert.Equal(HttpStatusCode.Conflict, deletarResponse.StatusCode);
        var aindaExisteResponse = await _client.GetAsync($"/eventos/{criado.Id}");
        Assert.Equal(HttpStatusCode.OK, aindaExisteResponse.StatusCode);
    }
}
```

Create `tests/RedeStore.IntegrationTests/Inscricoes/InscricaoFlowEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Inscricoes;

[Collection(IntegrationTestCollection.Name)]
public class InscricaoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public InscricaoFlowEndToEndTests(ApiFactory factory)
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

    private async Task<Guid> CriarEventoAsync(HttpClient clienteAdmin, int vagasTotais)
    {
        var criarResponse = await clienteAdmin.PostAsJsonAsync("/eventos", new CriarEventoRequest(
            Titulo: $"Retiro {Guid.NewGuid()}",
            Descricao: "Retiro anual",
            DataHora: DateTime.UtcNow.AddDays(30),
            Local: "Sítio da Rede",
            Preco: 50m,
            VagasTotais: vagasTotais,
            Foto: "https://exemplo.com/retiro.jpg"));
        var evento = await criarResponse.Content.ReadFromJsonAsync<EventoDto>();
        return evento!.Id;
    }

    [Fact]
    public async Task FluxoCompleto_InscreverListarCancelar_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var inscreverResponse = await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        inscreverResponse.EnsureSuccessStatusCode();
        var resultado = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        Assert.Equal("criada", resultado!.Resultado);
        Assert.Equal(50m, resultado.Inscricao!.ValorPago);

        var vagasResponse = await _client.GetAsync($"/eventos/{eventoId}/vagas-restantes");
        var vagas = await vagasResponse.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(9, vagas!.VagasRestantes);

        var minhasInscricoesResponse = await clienteJovem.GetAsync("/usuarios/me/inscricoes");
        minhasInscricoesResponse.EnsureSuccessStatusCode();
        var minhasInscricoes = await minhasInscricoesResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Single(minhasInscricoes!);

        var inscricoesDoEventoResponse = await clienteAdmin.GetAsync($"/eventos/{eventoId}/inscricoes");
        inscricoesDoEventoResponse.EnsureSuccessStatusCode();
        var inscricoesDoEvento = await inscricoesDoEventoResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Single(inscricoesDoEvento!);

        var cancelarResponse = await clienteJovem.PatchAsync($"/inscricoes/{resultado.Inscricao.Id}/cancelar", content: null);
        cancelarResponse.EnsureSuccessStatusCode();
        var cancelada = await cancelarResponse.Content.ReadFromJsonAsync<InscricaoDto>();
        Assert.Equal("cancelada", cancelada!.Status);

        var vagasAposCancelarResponse = await _client.GetAsync($"/eventos/{eventoId}/vagas-restantes");
        var vagasAposCancelar = await vagasAposCancelarResponse.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(10, vagasAposCancelar!.VagasRestantes);
    }

    [Fact]
    public async Task Inscrever_MesmoUsuarioDuasVezes_SegundaChamadaRetornaJaInscritoSemDuplicar()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var segundaResponse = await clienteJovem.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);

        segundaResponse.EnsureSuccessStatusCode();
        var resultado = await segundaResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        Assert.Equal("ja_inscrito", resultado!.Resultado);

        var minhasInscricoesResponse = await clienteJovem.GetAsync("/usuarios/me/inscricoes");
        var minhasInscricoes = await minhasInscricoesResponse.Content.ReadFromJsonAsync<List<InscricaoDto>>();
        Assert.Single(minhasInscricoes!);
    }

    [Fact]
    public async Task CancelarInscricao_PorUsuarioNaoDonoNaoAdmin_Retorna403()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var dono = await CriarClienteJovemAsync();
        var inscreverResponse = await dono.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var resultado = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();

        using var outroUsuario = await CriarClienteJovemAsync();
        var cancelarResponse = await outroUsuario.PatchAsync($"/inscricoes/{resultado!.Inscricao!.Id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, cancelarResponse.StatusCode);
    }

    [Fact]
    public async Task CancelarInscricao_PeloAdmin_Retorna200MesmoNaoSendoODono()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var dono = await CriarClienteJovemAsync();
        var inscreverResponse = await dono.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var resultado = await inscreverResponse.Content.ReadFromJsonAsync<ResultadoInscricaoDto>();

        var cancelarResponse = await clienteAdmin.PatchAsync($"/inscricoes/{resultado!.Inscricao!.Id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.OK, cancelarResponse.StatusCode);
    }

    [Fact]
    public async Task GetInscricoesDoEvento_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.GetAsync($"/eventos/{eventoId}/inscricoes");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DuasInscricoesSimultaneasNaUltimaVaga_ApenasUmaVemCriadaAOutraVemEsgotada()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var eventoId = await CriarEventoAsync(clienteAdmin, vagasTotais: 1);
        using var primeiroUsuario = await CriarClienteJovemAsync();
        using var segundoUsuario = await CriarClienteJovemAsync();

        var tarefaUm = primeiroUsuario.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        var tarefaDois = segundoUsuario.PostAsync($"/eventos/{eventoId}/inscricoes", content: null);
        await Task.WhenAll(tarefaUm, tarefaDois);

        var resultadoUm = await (await tarefaUm).Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        var resultadoDois = await (await tarefaDois).Content.ReadFromJsonAsync<ResultadoInscricaoDto>();
        var resultados = new[] { resultadoUm!.Resultado, resultadoDois!.Resultado };

        Assert.Single(resultados, r => r == "criada");
        Assert.Single(resultados, r => r == "esgotado");

        var vagasResponse = await _client.GetAsync($"/eventos/{eventoId}/vagas-restantes");
        var vagas = await vagasResponse.Content.ReadFromJsonAsync<VagasRestantesDto>();
        Assert.Equal(0, vagas!.VagasRestantes);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (if anything from Tasks 1-5 was missed, this is where it surfaces)**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~EventoFlowEndToEndTests|FullyQualifiedName~InscricaoFlowEndToEndTests"`
Expected: all of Tasks 1-5 are already implemented at this point, so this should already pass; if it doesn't, fix the gap it surfaces before proceeding (do not edit these tests to work around a production bug).

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~EventoFlowEndToEndTests|FullyQualifiedName~InscricaoFlowEndToEndTests"`
Expected: 9 passed. `DuasInscricoesSimultaneasNaUltimaVaga_ApenasUmaVemCriadaAOutraVemEsgotada` is the one that actually proves the `FOR UPDATE` lock works — it runs against the real Testcontainers Postgres with two genuinely parallel HTTP requests, which the in-memory fakes from Task 4's unit tests cannot exercise.

- [ ] **Step 4: Run the entire solution's test suite one final time**

Run: `dotnet test`
Expected: full solution green — this is the Fase 4 Definition of Done: agenda pública lista/filtra eventos futuros, admin CRUD completo, `vagasRestantes` correto em toda resposta relevante, inscrição idempotente, vagas esgotadas nunca dão overselling mesmo sob concorrência real, cancelamento por dono ou admin, e DELETE de evento bloqueado quando há inscrição confirmada.

- [ ] **Step 5: Commit**

```bash
git add tests/RedeStore.IntegrationTests/Eventos/EventoFlowEndToEndTests.cs tests/RedeStore.IntegrationTests/Inscricoes/InscricaoFlowEndToEndTests.cs
git commit -m "Add end-to-end tests covering the Fase 4 Definition of Done"
```
