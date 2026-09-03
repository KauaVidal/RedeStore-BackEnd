# Backend REDE — Fase 5 (Pedidos) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Pedidos subsystem — checkout with atomic stock decrement, "Meus Pedidos", and admin order management — on top of the Fase 1-4 foundation, matching the exact state machine and snapshot-pricing contract the frontend already expects.

**Architecture:** New `Pedido`/`ItemPedido`/`Endereco` entities (`Endereco` as an EF Core optional owned type in the `Pedidos` table, not its own table) and their EF Core migration; a new `IVariacaoRepository` (Application.Common) with a single `DecrementarEstoqueAsync` method that issues an atomic conditional `UPDATE ... WHERE "Estoque" >= @qtd` (no prior row lock needed — the conditional `UPDATE` itself is atomic under PostgreSQL); `IPedidoRepository` following the same shape as `IEventoRepository`/`IInscricaoRepository`; a single `PedidoService` that reuses the `IUnitOfWork`/`ITransacao` abstraction built in Fase 4 to wrap the whole checkout (all stock decrements + order creation) in one transaction — any single item's insufficient stock rolls back everything, no partial decrement; the state machine (`proximoStatus`) lives as a private static function inside `PedidoService`, exercised indirectly through `AvancarStatusAsync` tests, matching how `RecalcularTamanhosECores`/`CalcularVagasRestantes` were tested in Fases 3-4.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (already wired — `Database.ExecuteSqlInterpolatedAsync` used for the atomic stock decrement), FluentValidation (already wired), `Microsoft.AspNetCore.Mvc.Testing` (already wired), Testcontainers.PostgreSql (already wired — this is what proves the atomic decrement actually prevents overselling under real concurrent checkouts, the same way Fase 4's Testcontainers-backed test proved the vagas lock). No new NuGet packages needed for this phase.

**Spec:** `docs/2026-08-26-rede-backend-design.md` (section 3 "Modelo de dados" — `ItemPedido` snapshot fields, section 5 "Estoque de variação (checkout)", section 7, section 9 — "Fase 5 — Pedidos") and `docs/2026-08-26-rede-backend-requisitos.md` (section 1.5 "Pedido"/"ItemPedido"/"Endereco", section 3 "Controle de estoque na compra" — regras 1-3, section 6 "Pedidos (Checkout, Meus Pedidos, Admin)", section 8 "Resumo de autorização por papel").

## Global Constraints

- Target framework `net10.0` for every project (already set).
- Fixed dependency graph from Fases 1-4 (unchanged): `Domain` → nothing; `Application` → `Domain` only; `Infrastructure` → `Application` + `Domain`; `Api` → `Application` + `Infrastructure`. Repository **interfaces** live in `RedeStore.Application.Common`; **implementations** live in `RedeStore.Infrastructure.Persistence`/`RedeStore.Infrastructure.Persistence.Repositories`. `Application` must never reference `Infrastructure` or EF Core types directly.
- **Reuse, don't recreate:** `IUnitOfWork`/`ITransacao` (Application.Common) already exist from Fase 4 — `PedidoService.CriarAsync` opens a transaction with `IUnitOfWork.IniciarTransacaoAsync` exactly like `InscricaoService.InscreverAsync` does. `tests/RedeStore.UnitTests/Common/FakeUnitOfWork.cs` already exists (moved there during Fase 4's final-review fix wave) — do not recreate it, only reference it.
- **Stock decrement pattern (design doc §5, verbatim — do NOT add a `SELECT ... FOR UPDATE` lock here, that pattern is specific to Fase 4's vagas control and is unnecessary for this one):** `IVariacaoRepository.DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct) -> Task<bool>` issues `UPDATE "Variacoes" SET "Estoque" = "Estoque" - @qtd WHERE "Id" = @id AND "Estoque" >= @qtd` via `Database.ExecuteSqlInterpolatedAsync` and returns `true` only if at least one row was affected. The conditional `UPDATE` itself is atomic under PostgreSQL row-level locking — no separate lock step needed. Inside one transaction, `PedidoService.CriarAsync` calls this once per cart item; if ANY call returns `false`, throw `EstoqueInsuficienteException` immediately (do not process remaining items) — the `await using` transaction rolls back automatically on the unhandled exception, so no item is left partially decremented. Only after every item decrements successfully does the code build and persist the `Pedido`, then commit.
- `ItemPedido.ProdutoId` is a plain `Guid` column with **no FK to `Produto`** — do not add a `Produto` navigation property or a `HasOne<Produto>()` call to `ItemPedidoConfiguration`. A deleted product must not break historic orders; EF Core only creates an FK when you explicitly configure one, so simply not configuring it is sufficient.
- `Pedido.UsuarioId` **does** get an FK to `Usuarios` (`HasOne<Usuario>().WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Cascade)`, no navigation property) — apply this from the start. This mirrors the exact convention `PasswordResetTokenConfiguration`/`InscricaoConfiguration` already use; Fase 4's final review flagged the *absence* of this FK on `Inscricoes.UsuarioId` as an Important finding that had to be fixed after the fact — don't repeat that gap here.
- `Endereco` is an EF Core **optional owned type** mapped into the same `Pedidos` table (nullable columns when absent), configured via `builder.OwnsOne(p => p.Endereco, ...)` + `builder.Navigation(p => p.Endereco).IsRequired(false);`. It has no `Id` and is not its own entity/table. Do **not** call `.Include(p => p.Endereco)` anywhere — EF Core loads a same-table owned type automatically with its owner; an explicit `Include` on it is unnecessary (and calling it on a same-table owned navigation isn't the pattern this codebase uses anywhere else).
- `ItemPedido.PrecoUnitario` and `ItemPedido.Nome` are a **snapshot** of `Produto.Preco`/`Produto.Nome` read at order-creation time — never re-derived later, never trusted from the client. `ItemPedido.FotoUrl` is `produto.Fotos.FirstOrDefault() ?? string.Empty` (a documented decision: the requirements doc doesn't say which photo to snapshot when a product has several, so the first one is used).
- `Pedido.ValorTotal` is computed server-side as `itens.Sum(i => i.PrecoUnitario * i.Quantidade)` — **never** accepted from the client (the request payload for `POST /pedidos` has no price fields at all, by design).
- `StatusPedido` (`Pago`, `EmPreparo`, `Retirado`, `Entregue`) maps to wire strings with an **underscore** (`"pago"`, `"em_preparo"`, `"retirado"`, `"entregue"`) — `EmPreparo.ToString().ToLowerInvariant()` would incorrectly produce `"empreparo"`. Use an explicit `switch` expression for this mapping (persistence still uses `HasConversion<string>()` storing the plain enum name — only the DTO-facing mapping needs the underscore). `FormaEntrega` (`Retirada`, `Entrega`) has no such problem — `ToString().ToLowerInvariant()` is correct there, same convention as `CategoriaProduto`/`Papel`.
- **State machine** (`proximoStatus`, requisitos doc section 6, replicate exactly): `Pago` → `EmPreparo`; `EmPreparo` → `Retirado` (if `FormaEntrega == Retirada`) or `Entregue` (if `FormaEntrega == Entrega`); `Retirado` and `Entregue` are final — `AvancarStatusAsync` on either throws `PedidoEmEstadoFinalException` (409). `PATCH /pedidos/{id}/avancar-status` takes no request body — it only ever advances one step, never accepts an arbitrary target status.
- New `RedeStore.Domain.Exceptions` types, all following the exact `DomainException` shape from Fases 2-4 (override `Codigo`/`StatusCode`, single-`message` constructor): `PedidoNaoEncontradoException` (`"PEDIDO_NAO_ENCONTRADO"`, 404), `VariacaoNaoEncontradaException` (`"VARIACAO_NAO_ENCONTRADA"`, 404 — thrown when a `produtoId` exists but has no variação matching the requested `tamanho`+`cor`), `EstoqueInsuficienteException` (`"ESTOQUE_INSUFICIENTE"`, 409), `PedidoEmEstadoFinalException` (`"PEDIDO_EM_ESTADO_FINAL"`, 409). Reuse the existing `ProdutoNaoEncontradoException` (Fase 3) when `produtoId` itself doesn't exist. `GlobalExceptionHandler` needs no changes (handles any `DomainException` polymorphically).
- No mocking library. Unit tests needing a fake repository use small hand-rolled in-memory classes — not Moq/NSubstitute (same rule as every prior phase).
- `POST /pedidos` and `GET /usuarios/me/pedidos` require `.RequireAuthorization()` (any authenticated role); `GET /pedidos` and `PATCH /pedidos/{id}/avancar-status` require `.RequireAuthorization("Admin")` — per the authorization table in the requirements doc (section 8).
- `PedidosEndpoints.cs` holds all 4 routes for this feature in one file (including `/usuarios/me/pedidos`, which doesn't share the `/pedidos` route prefix) — mirrors the "one file per feature, not per route prefix" convention `InscricoesEndpoints.cs` established in Fase 4.
- **Test-double reuse across phases, without modifying earlier phases' test files:** `FakeVariacaoRepository` (this phase's tests) must NOT touch `tests/RedeStore.UnitTests/Produtos/FakeProdutoRepository.cs` (Fase 3, private dictionary). Instead it takes an `IProdutoRepository` in its constructor and calls `ListarAsync(null, null, ct)` (both filters `null` returns every stored product) to find the target `Variacao` by `Id` across all products, then mutates that `Variacao` object's `Estoque` field directly — this works because `Produto`/`Variacao` are reference types and `FakeProdutoRepository.ListarAsync` returns the same stored object references, not copies.
- Shared testing convention (same as every prior phase): integration tests share one Postgres container/database across the whole run via `ICollectionFixture`, so tests must not collide — use randomly-suffixed unique values wherever a test asserts on a specific row.

---

## Task 1: Pedido/ItemPedido/Endereco Persistence + IVariacaoRepository

**Files:**
- Create: `src/RedeStore.Domain/Entities/StatusPedido.cs`
- Create: `src/RedeStore.Domain/Entities/FormaEntrega.cs`
- Create: `src/RedeStore.Domain/Entities/Endereco.cs`
- Create: `src/RedeStore.Domain/Entities/Pedido.cs`
- Create: `src/RedeStore.Domain/Entities/ItemPedido.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/ItemPedidoConfiguration.cs`
- Modify: `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs`
- Create: `src/RedeStore.Application/Common/IPedidoRepository.cs`
- Create: `src/RedeStore.Application/Common/IVariacaoRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/PedidoRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/VariacaoRepository.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Migration: generated under `src/RedeStore.Infrastructure/Persistence/Migrations/`
- Test: `tests/RedeStore.IntegrationTests/Persistence/PedidoRepositoryTests.cs`
- Test: `tests/RedeStore.IntegrationTests/Persistence/VariacaoRepositoryTests.cs`

**Interfaces:**
- Consumes: `ApiFactory`/`IntegrationTestCollection` (Fase 2, unchanged), `IUsuarioRepository`/`IProdutoRepository` (Fases 2-3, unchanged, used only in this task's tests to seed a real `Usuario`/`Produto`).
- Produces: `RedeStore.Application.Common.IPedidoRepository` — `Task<List<Pedido>> ListarTodosAsync(CancellationToken ct)`, `Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)`, `Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct)`, `Task AdicionarAsync(Pedido pedido, CancellationToken ct)`, `Task AtualizarAsync(Pedido pedido, CancellationToken ct)`. `RedeStore.Application.Common.IVariacaoRepository` — `Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct)`. Both registered as `Scoped` in DI. Task 3 depends on these exact signatures.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Persistence/PedidoRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class PedidoRepositoryTests
{
    private readonly ApiFactory _factory;

    public PedidoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<Guid> CriarUsuarioDeTesteAsync(IUsuarioRepository usuarioRepositorio)
    {
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = "Cliente Teste",
            Email = $"{Guid.NewGuid()}@teste.com",
            Papel = Papel.Jovem,
            SenhaHash = "hash-fake",
        };
        await usuarioRepositorio.AdicionarAsync(usuario, CancellationToken.None);
        return usuario.Id;
    }

    private static Pedido CriarPedidoDeTeste(Guid usuarioId, Endereco? endereco = null) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = usuarioId,
        FormaEntrega = endereco is null ? FormaEntrega.Retirada : FormaEntrega.Entrega,
        Endereco = endereco,
        ValorTotal = 79.90m,
        Status = StatusPedido.Pago,
        CriadoEm = DateTime.UtcNow,
        Itens =
        [
            new ItemPedido
            {
                Id = Guid.NewGuid(),
                ProdutoId = Guid.NewGuid(),
                Nome = "Camiseta Rede",
                PrecoUnitario = 79.90m,
                FotoUrl = "https://exemplo.com/foto.jpg",
                Tamanho = "M",
                Cor = "Preto",
                Quantidade = 1,
            },
        ],
    };

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaPedidoComItens()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedido = CriarPedidoDeTeste(usuarioId);

        await pedidoRepositorio.AdicionarAsync(pedido, CancellationToken.None);
        var encontrado = await pedidoRepositorio.BuscarPorIdAsync(pedido.Id, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Single(encontrado!.Itens);
        Assert.Null(encontrado.Endereco);
    }

    [Fact]
    public async Task AdicionarAsync_ComEndereco_PersisteEnderecoCompleto()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var endereco = new Endereco { Rua = "Rua A", Numero = "123", Bairro = "Centro", Cidade = "São Paulo", Cep = "01000-000" };
        var pedido = CriarPedidoDeTeste(usuarioId, endereco);

        await pedidoRepositorio.AdicionarAsync(pedido, CancellationToken.None);
        var encontrado = await pedidoRepositorio.BuscarPorIdAsync(pedido.Id, CancellationToken.None);

        Assert.NotNull(encontrado!.Endereco);
        Assert.Equal("Rua A", encontrado.Endereco!.Rua);
        Assert.Null(encontrado.Endereco.Complemento);
    }

    [Fact]
    public async Task ListarPorUsuarioAsync_RetornaSoOsDoUsuarioMaisRecentesPrimeiro()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var outroUsuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedidoAntigo = CriarPedidoDeTeste(usuarioId);
        pedidoAntigo.CriadoEm = DateTime.UtcNow.AddDays(-1);
        var pedidoRecente = CriarPedidoDeTeste(usuarioId);
        var pedidoDeOutro = CriarPedidoDeTeste(outroUsuarioId);
        await pedidoRepositorio.AdicionarAsync(pedidoAntigo, CancellationToken.None);
        await pedidoRepositorio.AdicionarAsync(pedidoRecente, CancellationToken.None);
        await pedidoRepositorio.AdicionarAsync(pedidoDeOutro, CancellationToken.None);

        var resultado = await pedidoRepositorio.ListarPorUsuarioAsync(usuarioId, CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.Equal(pedidoRecente.Id, resultado[0].Id);
        Assert.Equal(pedidoAntigo.Id, resultado[1].Id);
    }

    [Fact]
    public async Task AtualizarAsync_AlteraOStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedido = CriarPedidoDeTeste(usuarioId);
        await pedidoRepositorio.AdicionarAsync(pedido, CancellationToken.None);

        pedido.Status = StatusPedido.EmPreparo;
        await pedidoRepositorio.AtualizarAsync(pedido, CancellationToken.None);
        var recarregado = await pedidoRepositorio.BuscarPorIdAsync(pedido.Id, CancellationToken.None);

        Assert.Equal(StatusPedido.EmPreparo, recarregado!.Status);
    }

    [Fact]
    public async Task ListarTodosAsync_RetornaDeTodosOsUsuarios()
    {
        using var scope = _factory.Services.CreateScope();
        var usuarioRepositorio = scope.ServiceProvider.GetRequiredService<IUsuarioRepository>();
        var pedidoRepositorio = scope.ServiceProvider.GetRequiredService<IPedidoRepository>();
        var usuarioUmId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var usuarioDoisId = await CriarUsuarioDeTesteAsync(usuarioRepositorio);
        var pedidoUm = CriarPedidoDeTeste(usuarioUmId);
        var pedidoDois = CriarPedidoDeTeste(usuarioDoisId);
        await pedidoRepositorio.AdicionarAsync(pedidoUm, CancellationToken.None);
        await pedidoRepositorio.AdicionarAsync(pedidoDois, CancellationToken.None);

        var resultado = await pedidoRepositorio.ListarTodosAsync(CancellationToken.None);

        Assert.Contains(resultado, p => p.Id == pedidoUm.Id);
        Assert.Contains(resultado, p => p.Id == pedidoDois.Id);
    }
}
```

Create `tests/RedeStore.IntegrationTests/Persistence/VariacaoRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class VariacaoRepositoryTests
{
    private readonly ApiFactory _factory;

    public VariacaoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<Variacao> CriarProdutoComVariacaoDeTesteAsync(IProdutoRepository produtoRepositorio, int estoque)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = $"Camiseta {Guid.NewGuid()}",
            Categoria = CategoriaProduto.Camisetas,
            Preco = 79.90m,
            Descricao = "Camiseta de teste",
            Fotos = ["https://exemplo.com/foto.jpg"],
            Tamanhos = ["M"],
            Cores = ["Preto"],
            Destaque = false,
            Variacoes = [new Variacao { Id = Guid.NewGuid(), Tamanho = "M", Cor = "Preto", Estoque = estoque }],
        };
        await produtoRepositorio.AdicionarAsync(produto, CancellationToken.None);
        return produto.Variacoes[0];
    }

    [Fact]
    public async Task DecrementarEstoqueAsync_ComEstoqueSuficiente_DecrementaERetornaTrue()
    {
        using var scope = _factory.Services.CreateScope();
        var produtoRepositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var variacaoRepositorio = scope.ServiceProvider.GetRequiredService<IVariacaoRepository>();
        var variacao = await CriarProdutoComVariacaoDeTesteAsync(produtoRepositorio, estoque: 10);

        var decrementou = await variacaoRepositorio.DecrementarEstoqueAsync(variacao.Id, 3, CancellationToken.None);

        Assert.True(decrementou);
        var produtoAtualizado = await produtoRepositorio.BuscarPorIdAsync(variacao.ProdutoId, CancellationToken.None);
        Assert.Equal(7, produtoAtualizado!.Variacoes[0].Estoque);
    }

    [Fact]
    public async Task DecrementarEstoqueAsync_ComEstoqueInsuficiente_NaoDecrementaERetornaFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var produtoRepositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var variacaoRepositorio = scope.ServiceProvider.GetRequiredService<IVariacaoRepository>();
        var variacao = await CriarProdutoComVariacaoDeTesteAsync(produtoRepositorio, estoque: 2);

        var decrementou = await variacaoRepositorio.DecrementarEstoqueAsync(variacao.Id, 3, CancellationToken.None);

        Assert.False(decrementou);
        var produtoAtualizado = await produtoRepositorio.BuscarPorIdAsync(variacao.ProdutoId, CancellationToken.None);
        Assert.Equal(2, produtoAtualizado!.Variacoes[0].Estoque);
    }

    [Fact]
    public async Task DecrementarEstoqueAsync_ComVariacaoInexistente_RetornaFalse()
    {
        using var scope = _factory.Services.CreateScope();
        var variacaoRepositorio = scope.ServiceProvider.GetRequiredService<IVariacaoRepository>();

        var decrementou = await variacaoRepositorio.DecrementarEstoqueAsync(Guid.NewGuid(), 1, CancellationToken.None);

        Assert.False(decrementou);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~PedidoRepositoryTests|FullyQualifiedName~VariacaoRepositoryTests"`
Expected: FAIL to compile — none of the entities/interfaces/repositories exist yet.

- [ ] **Step 3: Create the entities**

Create `src/RedeStore.Domain/Entities/StatusPedido.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public enum StatusPedido
{
    Pago,
    EmPreparo,
    Retirado,
    Entregue,
}
```

Create `src/RedeStore.Domain/Entities/FormaEntrega.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public enum FormaEntrega
{
    Retirada,
    Entrega,
}
```

Create `src/RedeStore.Domain/Entities/Endereco.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Endereco
{
    public required string Rua { get; set; }
    public required string Numero { get; set; }
    public string? Complemento { get; set; }
    public required string Bairro { get; set; }
    public required string Cidade { get; set; }
    public required string Cep { get; set; }
}
```

Create `src/RedeStore.Domain/Entities/Pedido.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Pedido
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public List<ItemPedido> Itens { get; set; } = [];
    public FormaEntrega FormaEntrega { get; set; }
    public Endereco? Endereco { get; set; }
    public decimal ValorTotal { get; set; }
    public StatusPedido Status { get; set; }
    public DateTime CriadoEm { get; set; }
}
```

Create `src/RedeStore.Domain/Entities/ItemPedido.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class ItemPedido
{
    public Guid Id { get; set; }
    public Guid PedidoId { get; set; }
    public Guid ProdutoId { get; set; }
    public required string Nome { get; set; }
    public decimal PrecoUnitario { get; set; }
    public required string FotoUrl { get; set; }
    public required string Tamanho { get; set; }
    public required string Cor { get; set; }
    public int Quantidade { get; set; }
}
```

- [ ] **Step 4: Configure the entities and wire them into `RedeStoreDbContext`**

Create `src/RedeStore.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class PedidoConfiguration : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> builder)
    {
        builder.ToTable("Pedidos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FormaEntrega).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.ValorTotal).HasPrecision(10, 2);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);

        builder.OwnsOne(p => p.Endereco, endereco =>
        {
            endereco.Property(e => e.Rua).HasMaxLength(200);
            endereco.Property(e => e.Numero).HasMaxLength(20);
            endereco.Property(e => e.Complemento).HasMaxLength(200);
            endereco.Property(e => e.Bairro).HasMaxLength(200);
            endereco.Property(e => e.Cidade).HasMaxLength(200);
            endereco.Property(e => e.Cep).HasMaxLength(20);
        });
        builder.Navigation(p => p.Endereco).IsRequired(false);
    }
}
```

Create `src/RedeStore.Infrastructure/Persistence/Configurations/ItemPedidoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class ItemPedidoConfiguration : IEntityTypeConfiguration<ItemPedido>
{
    public void Configure(EntityTypeBuilder<ItemPedido> builder)
    {
        builder.ToTable("ItensPedido");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Nome).IsRequired().HasMaxLength(200);
        builder.Property(i => i.PrecoUnitario).HasPrecision(10, 2);
        builder.Property(i => i.FotoUrl).IsRequired();
        builder.Property(i => i.Tamanho).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Cor).IsRequired().HasMaxLength(50);
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
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RedeStoreDbContext).Assembly);
    }
}
```

- [ ] **Step 5: Create the repository interfaces and implementations**

Create `src/RedeStore.Application/Common/IPedidoRepository.cs`:

```csharp
using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IPedidoRepository
{
    Task<List<Pedido>> ListarTodosAsync(CancellationToken ct);
    Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Pedido pedido, CancellationToken ct);
    Task AtualizarAsync(Pedido pedido, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Common/IVariacaoRepository.cs`:

```csharp
namespace RedeStore.Application.Common;

public interface IVariacaoRepository
{
    Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct);
}
```

Create `src/RedeStore.Infrastructure/Persistence/Repositories/PedidoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class PedidoRepository : IPedidoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public PedidoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Pedido>> ListarTodosAsync(CancellationToken ct) =>
        _dbContext.Pedidos.Include(p => p.Itens).OrderByDescending(p => p.CriadoEm).ToListAsync(ct);

    public Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        _dbContext.Pedidos.Include(p => p.Itens).Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.CriadoEm).ToListAsync(ct);

    public Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Pedidos.Include(p => p.Itens).SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AdicionarAsync(Pedido pedido, CancellationToken ct)
    {
        _dbContext.Pedidos.Add(pedido);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Pedido pedido, CancellationToken ct)
    {
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

`AtualizarAsync` does not call `.Update()` — the `Pedido` passed in always comes from `BuscarPorIdAsync` on the same `DbContext`, so it is already tracked; `SaveChangesAsync` alone persists the change. (Fase 4's final review found the opposite pattern — an unnecessary `.Update()` on an already-tracked entity — to be a real bug, since it force-marks unrelated child rows `Modified`. Don't reintroduce that here.)

Create `src/RedeStore.Infrastructure/Persistence/Repositories/VariacaoRepository.cs`:

```csharp
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class VariacaoRepository : IVariacaoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public VariacaoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct)
    {
        var linhasAfetadas = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Variacoes\" SET \"Estoque\" = \"Estoque\" - {quantidade} WHERE \"Id\" = {variacaoId} AND \"Estoque\" >= {quantidade}", ct);
        return linhasAfetadas > 0;
    }
}
```

- [ ] **Step 6: Register the repositories in DI**

Open `src/RedeStore.Api/Program.cs` and add these lines right after the `IUnitOfWork` registration (keep everything else in the file unchanged):

```csharp
builder.Services.AddScoped<IPedidoRepository, PedidoRepository>();
builder.Services.AddScoped<IVariacaoRepository, VariacaoRepository>();
```

No new `using` statements are needed — `RedeStore.Application.Common` and `RedeStore.Infrastructure.Persistence.Repositories` are already imported.

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add AdicionarPedidos --project src/RedeStore.Infrastructure --startup-project src/RedeStore.Api --output-dir Persistence/Migrations
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~PedidoRepositoryTests|FullyQualifiedName~VariacaoRepositoryTests"`
Expected: 8 passed. (`ApiFactory.InitializeAsync` applies the new `AdicionarPedidos` migration automatically.)

- [ ] **Step 9: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 10: Commit**

```bash
git add src/RedeStore.Domain/Entities/StatusPedido.cs src/RedeStore.Domain/Entities/FormaEntrega.cs src/RedeStore.Domain/Entities/Endereco.cs src/RedeStore.Domain/Entities/Pedido.cs src/RedeStore.Domain/Entities/ItemPedido.cs src/RedeStore.Infrastructure/Persistence/Configurations/PedidoConfiguration.cs src/RedeStore.Infrastructure/Persistence/Configurations/ItemPedidoConfiguration.cs src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs src/RedeStore.Application/Common/IPedidoRepository.cs src/RedeStore.Application/Common/IVariacaoRepository.cs src/RedeStore.Infrastructure/Persistence/Repositories/PedidoRepository.cs src/RedeStore.Infrastructure/Persistence/Repositories/VariacaoRepository.cs src/RedeStore.Infrastructure/Persistence/Migrations/ src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Persistence/PedidoRepositoryTests.cs tests/RedeStore.IntegrationTests/Persistence/VariacaoRepositoryTests.cs
git commit -m "Add Pedido/ItemPedido/Endereco persistence and atomic stock-decrement repository"
```

---

## Task 2: Pedido DTOs and FluentValidation Validators

**Files:**
- Create: `src/RedeStore.Application/Pedidos/Dtos/ItemPedidoRequest.cs`
- Create: `src/RedeStore.Application/Pedidos/Dtos/EnderecoDto.cs`
- Create: `src/RedeStore.Application/Pedidos/Dtos/CriarPedidoRequest.cs`
- Create: `src/RedeStore.Application/Pedidos/Dtos/ItemPedidoDto.cs`
- Create: `src/RedeStore.Application/Pedidos/Dtos/PedidoDto.cs`
- Create: `src/RedeStore.Application/Pedidos/Validators/CriarPedidoRequestValidator.cs`
- Test: `tests/RedeStore.UnitTests/Pedidos/Validators/CriarPedidoRequestValidatorTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 directly (DTOs are plain data shapes).
- Produces: `ItemPedidoRequest(Guid ProdutoId, string Tamanho, string Cor, int Quantidade)`, `EnderecoDto(string Rua, string Numero, string? Complemento, string Bairro, string Cidade, string Cep)` (used for both request and response — the shape is identical in both directions, so one type serves both, unlike `VariacaoRequest`/`VariacaoDto` in Fase 3 which differ by the server-generated `Id`), `CriarPedidoRequest(List<ItemPedidoRequest> Itens, string FormaEntrega, EnderecoDto? Endereco)`, `ItemPedidoDto(Guid ProdutoId, string Nome, decimal PrecoUnitario, string FotoUrl, string Tamanho, string Cor, int Quantidade)`, `PedidoDto(Guid Id, Guid UsuarioId, List<ItemPedidoDto> Itens, string FormaEntrega, EnderecoDto? Endereco, decimal ValorTotal, string Status, DateTime CriadoEm)`. `FormaEntrega`/`Status` on `PedidoDto` are the lowercase wire strings, never the enum. Task 3 and Task 4 depend on these exact shapes; Task 4 wires `CriarPedidoRequestValidator` into `ValidationFilter<T>` (Fase 2).

- [ ] **Step 1: Write the failing validator tests**

Create `tests/RedeStore.UnitTests/Pedidos/Validators/CriarPedidoRequestValidatorTests.cs`:

```csharp
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Pedidos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Pedidos.Validators;

public class CriarPedidoRequestValidatorTests
{
    private readonly CriarPedidoRequestValidator _validator = new();

    private static CriarPedidoRequest RequestValido() => new(
        Itens: [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 1)],
        FormaEntrega: "retirada",
        Endereco: null);

    [Fact]
    public void Validate_ComRequestValidoRetirada_NaoRetornaErros()
    {
        var resultado = _validator.Validate(RequestValido());

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComItensVazios_RetornaErro()
    {
        var request = RequestValido() with { Itens = [] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComQuantidadeZero_RetornaErro()
    {
        var request = RequestValido() with { Itens = [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 0)] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFormaEntregaInvalida_RetornaErro()
    {
        var request = RequestValido() with { FormaEntrega = "teleporte" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFormaEntregaEntregaSemEndereco_RetornaErro()
    {
        var request = RequestValido() with { FormaEntrega = "entrega", Endereco = null };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComFormaEntregaEntregaEEnderecoCompleto_NaoRetornaErros()
    {
        var endereco = new EnderecoDto("Rua A", "1", null, "Centro", "SP", "01000-000");
        var request = RequestValido() with { FormaEntrega = "entrega", Endereco = endereco };

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComEnderecoComCepVazio_RetornaErro()
    {
        var endereco = new EnderecoDto("Rua A", "1", null, "Centro", "SP", "");
        var request = RequestValido() with { FormaEntrega = "entrega", Endereco = endereco };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~CriarPedidoRequestValidatorTests"`
Expected: FAIL to compile — none of the DTOs/validators exist yet.

- [ ] **Step 3: Create the DTOs**

Create `src/RedeStore.Application/Pedidos/Dtos/ItemPedidoRequest.cs`:

```csharp
namespace RedeStore.Application.Pedidos.Dtos;

public sealed record ItemPedidoRequest(Guid ProdutoId, string Tamanho, string Cor, int Quantidade);
```

Create `src/RedeStore.Application/Pedidos/Dtos/EnderecoDto.cs`:

```csharp
namespace RedeStore.Application.Pedidos.Dtos;

public sealed record EnderecoDto(string Rua, string Numero, string? Complemento, string Bairro, string Cidade, string Cep);
```

Create `src/RedeStore.Application/Pedidos/Dtos/CriarPedidoRequest.cs`:

```csharp
namespace RedeStore.Application.Pedidos.Dtos;

public sealed record CriarPedidoRequest(List<ItemPedidoRequest> Itens, string FormaEntrega, EnderecoDto? Endereco);
```

Create `src/RedeStore.Application/Pedidos/Dtos/ItemPedidoDto.cs`:

```csharp
namespace RedeStore.Application.Pedidos.Dtos;

public sealed record ItemPedidoDto(
    Guid ProdutoId,
    string Nome,
    decimal PrecoUnitario,
    string FotoUrl,
    string Tamanho,
    string Cor,
    int Quantidade);
```

Create `src/RedeStore.Application/Pedidos/Dtos/PedidoDto.cs`:

```csharp
namespace RedeStore.Application.Pedidos.Dtos;

public sealed record PedidoDto(
    Guid Id,
    Guid UsuarioId,
    List<ItemPedidoDto> Itens,
    string FormaEntrega,
    EnderecoDto? Endereco,
    decimal ValorTotal,
    string Status,
    DateTime CriadoEm);
```

- [ ] **Step 4: Implement the validator**

Create `src/RedeStore.Application/Pedidos/Validators/CriarPedidoRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Pedidos.Dtos;

namespace RedeStore.Application.Pedidos.Validators;

public sealed class CriarPedidoRequestValidator : AbstractValidator<CriarPedidoRequest>
{
    private static readonly string[] FormasEntregaValidas = ["retirada", "entrega"];

    public CriarPedidoRequestValidator()
    {
        RuleFor(r => r.Itens)
            .Must(itens => itens is not null && itens.Count > 0)
            .WithMessage("O pedido precisa de ao menos 1 item.");
        RuleForEach(r => r.Itens).ChildRules(item =>
        {
            item.RuleFor(i => i.ProdutoId).NotEmpty();
            item.RuleFor(i => i.Tamanho).NotEmpty();
            item.RuleFor(i => i.Cor).NotEmpty();
            item.RuleFor(i => i.Quantidade).GreaterThan(0);
        });
        RuleFor(r => r.FormaEntrega)
            .Must(f => FormasEntregaValidas.Contains(f))
            .WithMessage("FormaEntrega deve ser 'retirada' ou 'entrega'.");
        RuleFor(r => r.Endereco)
            .NotNull()
            .When(r => r.FormaEntrega == "entrega")
            .WithMessage("Endereco é obrigatório quando formaEntrega é 'entrega'.");
        When(r => r.Endereco is not null, () =>
        {
            RuleFor(r => r.Endereco!.Rua).NotEmpty();
            RuleFor(r => r.Endereco!.Numero).NotEmpty();
            RuleFor(r => r.Endereco!.Bairro).NotEmpty();
            RuleFor(r => r.Endereco!.Cidade).NotEmpty();
            RuleFor(r => r.Endereco!.Cep).NotEmpty();
        });
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~CriarPedidoRequestValidatorTests"`
Expected: 7 passed.

- [ ] **Step 6: Commit**

```bash
git add src/RedeStore.Application/Pedidos/Dtos/ src/RedeStore.Application/Pedidos/Validators/ tests/RedeStore.UnitTests/Pedidos/Validators/
git commit -m "Add Pedido DTOs and FluentValidation validator"
```

---

## Task 3: PedidoService

**Files:**
- Create: `src/RedeStore.Domain/Exceptions/PedidoNaoEncontradoException.cs`
- Create: `src/RedeStore.Domain/Exceptions/VariacaoNaoEncontradaException.cs`
- Create: `src/RedeStore.Domain/Exceptions/EstoqueInsuficienteException.cs`
- Create: `src/RedeStore.Domain/Exceptions/PedidoEmEstadoFinalException.cs`
- Create: `src/RedeStore.Application/Pedidos/IPedidoService.cs`
- Create: `src/RedeStore.Application/Pedidos/PedidoService.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Domain/PedidoNaoEncontradoExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Domain/VariacaoNaoEncontradaExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Domain/EstoqueInsuficienteExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Domain/PedidoEmEstadoFinalExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Pedidos/FakePedidoRepository.cs`
- Test: `tests/RedeStore.UnitTests/Pedidos/FakeVariacaoRepository.cs`
- Test: `tests/RedeStore.UnitTests/Pedidos/PedidoServiceTests.cs`

**Interfaces:**
- Consumes: `IProdutoRepository` (Fase 3, unchanged — `BuscarPorIdAsync` returns a `Produto` with `Variacoes` populated), `IVariacaoRepository`/`IPedidoRepository`/`IUnitOfWork` (Task 1/Fase 4), the DTOs from Task 2, `tests/RedeStore.UnitTests/Produtos/FakeProdutoRepository.cs` (Fase 3, reused unmodified), `tests/RedeStore.UnitTests/Common/FakeUnitOfWork.cs` (Fase 4, reused unmodified).
- Produces: `RedeStore.Application.Pedidos.IPedidoService` — `Task<PedidoDto> CriarAsync(Guid usuarioId, CriarPedidoRequest request, CancellationToken ct)`, `Task<List<PedidoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)`, `Task<List<PedidoDto>> ListarTodosAsync(CancellationToken ct)`, `Task<PedidoDto> AvancarStatusAsync(Guid id, CancellationToken ct)`. Four new `RedeStore.Domain.Exceptions` types (`PedidoNaoEncontradoException` 404, `VariacaoNaoEncontradaException` 404, `EstoqueInsuficienteException` 409, `PedidoEmEstadoFinalException` 409). `tests/RedeStore.UnitTests/Pedidos/FakeVariacaoRepository.cs` — constructor `FakeVariacaoRepository(IProdutoRepository produtoRepository)`. Task 4 maps all `IPedidoService` methods to HTTP endpoints and relies on all four exceptions producing their status codes via the existing `GlobalExceptionHandler`.

- [ ] **Step 1: Write the failing exception tests**

Create `tests/RedeStore.UnitTests/Domain/PedidoNaoEncontradoExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class PedidoNaoEncontradoExceptionTests
{
    [Fact]
    public void PedidoNaoEncontradoException_TemCodigoEStatusCorretos()
    {
        var exception = new PedidoNaoEncontradoException("pedido não encontrado");

        Assert.Equal("PEDIDO_NAO_ENCONTRADO", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
```

Create `tests/RedeStore.UnitTests/Domain/VariacaoNaoEncontradaExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class VariacaoNaoEncontradaExceptionTests
{
    [Fact]
    public void VariacaoNaoEncontradaException_TemCodigoEStatusCorretos()
    {
        var exception = new VariacaoNaoEncontradaException("variação não encontrada");

        Assert.Equal("VARIACAO_NAO_ENCONTRADA", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
```

Create `tests/RedeStore.UnitTests/Domain/EstoqueInsuficienteExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class EstoqueInsuficienteExceptionTests
{
    [Fact]
    public void EstoqueInsuficienteException_TemCodigoEStatusCorretos()
    {
        var exception = new EstoqueInsuficienteException("estoque insuficiente");

        Assert.Equal("ESTOQUE_INSUFICIENTE", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
```

Create `tests/RedeStore.UnitTests/Domain/PedidoEmEstadoFinalExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class PedidoEmEstadoFinalExceptionTests
{
    [Fact]
    public void PedidoEmEstadoFinalException_TemCodigoEStatusCorretos()
    {
        var exception = new PedidoEmEstadoFinalException("pedido em estado final");

        Assert.Equal("PEDIDO_EM_ESTADO_FINAL", exception.Codigo);
        Assert.Equal(409, exception.StatusCode);
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/RedeStore.UnitTests/Pedidos/FakePedidoRepository.cs` (an in-memory test double, not a mock — Global Constraints forbid mocking libraries):

```csharp
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Pedidos;

public sealed class FakePedidoRepository : IPedidoRepository
{
    private readonly Dictionary<Guid, Pedido> _pedidosPorId = new();

    public Task<List<Pedido>> ListarTodosAsync(CancellationToken ct) =>
        Task.FromResult(_pedidosPorId.Values.OrderByDescending(p => p.CriadoEm).ToList());

    public Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(_pedidosPorId.Values.Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.CriadoEm).ToList());

    public Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_pedidosPorId.GetValueOrDefault(id));

    public Task AdicionarAsync(Pedido pedido, CancellationToken ct)
    {
        _pedidosPorId[pedido.Id] = pedido;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Pedido pedido, CancellationToken ct)
    {
        _pedidosPorId[pedido.Id] = pedido;
        return Task.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Pedidos/FakeVariacaoRepository.cs`:

```csharp
using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Pedidos;

public sealed class FakeVariacaoRepository : IVariacaoRepository
{
    private readonly IProdutoRepository _produtoRepository;

    public FakeVariacaoRepository(IProdutoRepository produtoRepository)
    {
        _produtoRepository = produtoRepository;
    }

    public async Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct)
    {
        var produtos = await _produtoRepository.ListarAsync(null, null, ct);
        var variacao = produtos.SelectMany(p => p.Variacoes).SingleOrDefault(v => v.Id == variacaoId);

        if (variacao is null || variacao.Estoque < quantidade)
        {
            return false;
        }

        variacao.Estoque -= quantidade;
        return true;
    }
}
```

Create `tests/RedeStore.UnitTests/Pedidos/PedidoServiceTests.cs`:

```csharp
using RedeStore.Application.Pedidos;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;
using RedeStore.UnitTests.Common;
using RedeStore.UnitTests.Produtos;
using Xunit;

namespace RedeStore.UnitTests.Pedidos;

public class PedidoServiceTests
{
    private readonly FakeProdutoRepository _produtoRepositorio = new();
    private readonly FakeVariacaoRepository _variacaoRepositorio;
    private readonly FakePedidoRepository _pedidoRepositorio = new();
    private readonly PedidoService _sut;

    public PedidoServiceTests()
    {
        _variacaoRepositorio = new FakeVariacaoRepository(_produtoRepositorio);
        _sut = new PedidoService(_produtoRepositorio, _variacaoRepositorio, _pedidoRepositorio, new FakeUnitOfWork());
    }

    private async Task<Guid> CriarProdutoComVariacaoAsync(int estoque = 10, decimal preco = 79.90m)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = "Camiseta Rede",
            Categoria = CategoriaProduto.Camisetas,
            Preco = preco,
            Descricao = "Camiseta oficial",
            Fotos = ["https://exemplo.com/foto.jpg"],
            Tamanhos = ["M"],
            Cores = ["Preto"],
            Destaque = false,
            Variacoes = [new Variacao { Id = Guid.NewGuid(), Tamanho = "M", Cor = "Preto", Estoque = estoque }],
        };
        await _produtoRepositorio.AdicionarAsync(produto, CancellationToken.None);
        return produto.Id;
    }

    private static CriarPedidoRequest RequestValido(Guid produtoId, int quantidade = 1, string formaEntrega = "retirada", EnderecoDto? endereco = null) => new(
        Itens: [new ItemPedidoRequest(produtoId, "M", "Preto", quantidade)],
        FormaEntrega: formaEntrega,
        Endereco: endereco);

    [Fact]
    public async Task CriarAsync_ComEstoqueDisponivel_CriaPedidoComSnapshotDoProduto()
    {
        var produtoId = await CriarProdutoComVariacaoAsync(estoque: 10, preco: 79.90m);

        var pedido = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId), CancellationToken.None);

        Assert.Equal("pago", pedido.Status);
        Assert.Single(pedido.Itens);
        Assert.Equal("Camiseta Rede", pedido.Itens[0].Nome);
        Assert.Equal(79.90m, pedido.Itens[0].PrecoUnitario);
        Assert.Equal(79.90m, pedido.ValorTotal);
    }

    [Fact]
    public async Task CriarAsync_DecrementaOEstoqueDaVariacao()
    {
        var produtoId = await CriarProdutoComVariacaoAsync(estoque: 10);

        await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, quantidade: 3), CancellationToken.None);

        var produtoAtualizado = await _produtoRepositorio.BuscarPorIdAsync(produtoId, CancellationToken.None);
        Assert.Equal(7, produtoAtualizado!.Variacoes[0].Estoque);
    }

    [Fact]
    public async Task CriarAsync_ComProdutoInexistente_LancaProdutoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<ProdutoNaoEncontradoException>(() =>
            _sut.CriarAsync(Guid.NewGuid(), RequestValido(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task CriarAsync_ComTamanhoCorInexistentes_LancaVariacaoNaoEncontradaException()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var request = RequestValido(produtoId) with { Itens = [new ItemPedidoRequest(produtoId, "GG", "Verde", 1)] };

        await Assert.ThrowsAsync<VariacaoNaoEncontradaException>(() =>
            _sut.CriarAsync(Guid.NewGuid(), request, CancellationToken.None));
    }

    [Fact]
    public async Task CriarAsync_ComEstoqueInsuficiente_LancaEstoqueInsuficienteExceptionENaoCriaPedido()
    {
        var produtoId = await CriarProdutoComVariacaoAsync(estoque: 2);

        await Assert.ThrowsAsync<EstoqueInsuficienteException>(() =>
            _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, quantidade: 3), CancellationToken.None));

        var pedidos = await _sut.ListarTodosAsync(CancellationToken.None);
        Assert.Empty(pedidos);
    }

    [Fact]
    public async Task CriarAsync_ComFormaEntregaRetirada_IgnoraEnderecoMesmoQueEnviado()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var endereco = new EnderecoDto("Rua A", "1", null, "Centro", "SP", "01000-000");

        var pedido = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, formaEntrega: "retirada", endereco: endereco), CancellationToken.None);

        Assert.Null(pedido.Endereco);
    }

    [Fact]
    public async Task CriarAsync_ComFormaEntregaEntrega_PersisteEndereco()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var endereco = new EnderecoDto("Rua A", "1", "Apto 2", "Centro", "SP", "01000-000");

        var pedido = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, formaEntrega: "entrega", endereco: endereco), CancellationToken.None);

        Assert.NotNull(pedido.Endereco);
        Assert.Equal("Apto 2", pedido.Endereco!.Complemento);
    }

    [Fact]
    public async Task CriarAsync_ComMultiplosItens_SomaOValorTotal()
    {
        var produtoUmId = await CriarProdutoComVariacaoAsync(estoque: 10, preco: 50m);
        var produtoDoisId = await CriarProdutoComVariacaoAsync(estoque: 10, preco: 30m);
        var request = new CriarPedidoRequest(
            Itens: [new ItemPedidoRequest(produtoUmId, "M", "Preto", 2), new ItemPedidoRequest(produtoDoisId, "M", "Preto", 1)],
            FormaEntrega: "retirada",
            Endereco: null);

        var pedido = await _sut.CriarAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(130m, pedido.ValorTotal);
    }

    [Fact]
    public async Task AvancarStatusAsync_DePagoParaEmPreparo()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var criado = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId), CancellationToken.None);

        var atualizado = await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);

        Assert.Equal("em_preparo", atualizado.Status);
    }

    [Fact]
    public async Task AvancarStatusAsync_DeEmPreparoParaRetirado_QuandoFormaEntregaRetirada()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var criado = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, formaEntrega: "retirada"), CancellationToken.None);
        await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);

        var atualizado = await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);

        Assert.Equal("retirado", atualizado.Status);
    }

    [Fact]
    public async Task AvancarStatusAsync_DeEmPreparoParaEntregue_QuandoFormaEntregaEntrega()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var endereco = new EnderecoDto("Rua A", "1", null, "Centro", "SP", "01000-000");
        var criado = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, formaEntrega: "entrega", endereco: endereco), CancellationToken.None);
        await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);

        var atualizado = await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);

        Assert.Equal("entregue", atualizado.Status);
    }

    [Fact]
    public async Task AvancarStatusAsync_EmEstadoFinal_LancaPedidoEmEstadoFinalException()
    {
        var produtoId = await CriarProdutoComVariacaoAsync();
        var criado = await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId, formaEntrega: "retirada"), CancellationToken.None);
        await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);
        await _sut.AvancarStatusAsync(criado.Id, CancellationToken.None);

        await Assert.ThrowsAsync<PedidoEmEstadoFinalException>(() =>
            _sut.AvancarStatusAsync(criado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task AvancarStatusAsync_ComIdInexistente_LancaPedidoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<PedidoNaoEncontradoException>(() =>
            _sut.AvancarStatusAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ListarPorUsuarioAsync_RetornaSoOsDoUsuario()
    {
        var produtoId = await CriarProdutoComVariacaoAsync(estoque: 10);
        var usuarioId = Guid.NewGuid();
        await _sut.CriarAsync(usuarioId, RequestValido(produtoId), CancellationToken.None);
        await _sut.CriarAsync(Guid.NewGuid(), RequestValido(produtoId), CancellationToken.None);

        var resultado = await _sut.ListarPorUsuarioAsync(usuarioId, CancellationToken.None);

        Assert.Single(resultado);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~PedidoNaoEncontradoExceptionTests|FullyQualifiedName~VariacaoNaoEncontradaExceptionTests|FullyQualifiedName~EstoqueInsuficienteExceptionTests|FullyQualifiedName~PedidoEmEstadoFinalExceptionTests|FullyQualifiedName~PedidoServiceTests"`
Expected: FAIL to compile — none of the exceptions/`IPedidoService`/`PedidoService` exist yet.

- [ ] **Step 4: Implement the exceptions**

Create `src/RedeStore.Domain/Exceptions/PedidoNaoEncontradoException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class PedidoNaoEncontradoException : DomainException
{
    public override string Codigo => "PEDIDO_NAO_ENCONTRADO";
    public override int StatusCode => 404;

    public PedidoNaoEncontradoException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/VariacaoNaoEncontradaException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class VariacaoNaoEncontradaException : DomainException
{
    public override string Codigo => "VARIACAO_NAO_ENCONTRADA";
    public override int StatusCode => 404;

    public VariacaoNaoEncontradaException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/EstoqueInsuficienteException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class EstoqueInsuficienteException : DomainException
{
    public override string Codigo => "ESTOQUE_INSUFICIENTE";
    public override int StatusCode => 409;

    public EstoqueInsuficienteException(string message) : base(message)
    {
    }
}
```

Create `src/RedeStore.Domain/Exceptions/PedidoEmEstadoFinalException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class PedidoEmEstadoFinalException : DomainException
{
    public override string Codigo => "PEDIDO_EM_ESTADO_FINAL";
    public override int StatusCode => 409;

    public PedidoEmEstadoFinalException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 5: Implement `IPedidoService` and `PedidoService`**

Create `src/RedeStore.Application/Pedidos/IPedidoService.cs`:

```csharp
using RedeStore.Application.Pedidos.Dtos;

namespace RedeStore.Application.Pedidos;

public interface IPedidoService
{
    Task<PedidoDto> CriarAsync(Guid usuarioId, CriarPedidoRequest request, CancellationToken ct);
    Task<List<PedidoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<List<PedidoDto>> ListarTodosAsync(CancellationToken ct);
    Task<PedidoDto> AvancarStatusAsync(Guid id, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Pedidos/PedidoService.cs`:

```csharp
using RedeStore.Application.Common;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Pedidos;

public sealed class PedidoService : IPedidoService
{
    private readonly IProdutoRepository _produtoRepository;
    private readonly IVariacaoRepository _variacaoRepository;
    private readonly IPedidoRepository _pedidoRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PedidoService(
        IProdutoRepository produtoRepository,
        IVariacaoRepository variacaoRepository,
        IPedidoRepository pedidoRepository,
        IUnitOfWork unitOfWork)
    {
        _produtoRepository = produtoRepository;
        _variacaoRepository = variacaoRepository;
        _pedidoRepository = pedidoRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<PedidoDto> CriarAsync(Guid usuarioId, CriarPedidoRequest request, CancellationToken ct)
    {
        var formaEntrega = ParseFormaEntregaObrigatoria(request.FormaEntrega);

        await using var transacao = await _unitOfWork.IniciarTransacaoAsync(ct);

        var itensPedido = new List<ItemPedido>();
        foreach (var itemRequest in request.Itens)
        {
            var produto = await _produtoRepository.BuscarPorIdAsync(itemRequest.ProdutoId, ct)
                ?? throw new ProdutoNaoEncontradoException($"Produto '{itemRequest.ProdutoId}' não encontrado.");

            var variacao = produto.Variacoes.SingleOrDefault(v => v.Tamanho == itemRequest.Tamanho && v.Cor == itemRequest.Cor)
                ?? throw new VariacaoNaoEncontradaException(
                    $"Produto '{produto.Nome}' não tem variação {itemRequest.Tamanho}/{itemRequest.Cor}.");

            var decrementou = await _variacaoRepository.DecrementarEstoqueAsync(variacao.Id, itemRequest.Quantidade, ct);
            if (!decrementou)
            {
                throw new EstoqueInsuficienteException(
                    $"Estoque insuficiente para '{produto.Nome}' ({itemRequest.Tamanho}/{itemRequest.Cor}).");
            }

            itensPedido.Add(new ItemPedido
            {
                Id = Guid.NewGuid(),
                ProdutoId = produto.Id,
                Nome = produto.Nome,
                PrecoUnitario = produto.Preco,
                FotoUrl = produto.Fotos.FirstOrDefault() ?? string.Empty,
                Tamanho = itemRequest.Tamanho,
                Cor = itemRequest.Cor,
                Quantidade = itemRequest.Quantidade,
            });
        }

        var pedido = new Pedido
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Itens = itensPedido,
            FormaEntrega = formaEntrega,
            Endereco = formaEntrega == FormaEntrega.Entrega ? MapearEndereco(request.Endereco!) : null,
            ValorTotal = itensPedido.Sum(i => i.PrecoUnitario * i.Quantidade),
            Status = StatusPedido.Pago,
            CriadoEm = DateTime.UtcNow,
        };

        await _pedidoRepository.AdicionarAsync(pedido, ct);
        await transacao.ConfirmarAsync(ct);

        return MapearParaDto(pedido);
    }

    public async Task<List<PedidoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)
    {
        var pedidos = await _pedidoRepository.ListarPorUsuarioAsync(usuarioId, ct);
        return pedidos.Select(MapearParaDto).ToList();
    }

    public async Task<List<PedidoDto>> ListarTodosAsync(CancellationToken ct)
    {
        var pedidos = await _pedidoRepository.ListarTodosAsync(ct);
        return pedidos.Select(MapearParaDto).ToList();
    }

    public async Task<PedidoDto> AvancarStatusAsync(Guid id, CancellationToken ct)
    {
        var pedido = await _pedidoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new PedidoNaoEncontradoException($"Pedido '{id}' não encontrado.");

        var proximo = ProximoStatus(pedido.Status, pedido.FormaEntrega)
            ?? throw new PedidoEmEstadoFinalException($"Pedido '{id}' já está em um estado final.");

        pedido.Status = proximo;
        await _pedidoRepository.AtualizarAsync(pedido, ct);
        return MapearParaDto(pedido);
    }

    private static StatusPedido? ProximoStatus(StatusPedido atual, FormaEntrega formaEntrega) => atual switch
    {
        StatusPedido.Pago => StatusPedido.EmPreparo,
        StatusPedido.EmPreparo => formaEntrega == FormaEntrega.Retirada ? StatusPedido.Retirado : StatusPedido.Entregue,
        StatusPedido.Retirado => null,
        StatusPedido.Entregue => null,
        _ => throw new InvalidOperationException($"Status '{atual}' desconhecido."),
    };

    private static FormaEntrega ParseFormaEntregaObrigatoria(string valor) => valor switch
    {
        "retirada" => FormaEntrega.Retirada,
        "entrega" => FormaEntrega.Entrega,
        _ => throw new InvalidOperationException($"FormaEntrega '{valor}' inválida."),
    };

    private static string StatusParaTexto(StatusPedido status) => status switch
    {
        StatusPedido.Pago => "pago",
        StatusPedido.EmPreparo => "em_preparo",
        StatusPedido.Retirado => "retirado",
        StatusPedido.Entregue => "entregue",
        _ => throw new InvalidOperationException($"Status '{status}' desconhecido."),
    };

    private static Endereco MapearEndereco(EnderecoDto dto) => new()
    {
        Rua = dto.Rua,
        Numero = dto.Numero,
        Complemento = dto.Complemento,
        Bairro = dto.Bairro,
        Cidade = dto.Cidade,
        Cep = dto.Cep,
    };

    private static EnderecoDto? MapearEnderecoParaDto(Endereco? endereco) => endereco is null
        ? null
        : new EnderecoDto(endereco.Rua, endereco.Numero, endereco.Complemento, endereco.Bairro, endereco.Cidade, endereco.Cep);

    private static PedidoDto MapearParaDto(Pedido pedido) => new(
        pedido.Id,
        pedido.UsuarioId,
        pedido.Itens.Select(i => new ItemPedidoDto(i.ProdutoId, i.Nome, i.PrecoUnitario, i.FotoUrl, i.Tamanho, i.Cor, i.Quantidade)).ToList(),
        pedido.FormaEntrega.ToString().ToLowerInvariant(),
        MapearEnderecoParaDto(pedido.Endereco),
        pedido.ValorTotal,
        StatusParaTexto(pedido.Status),
        pedido.CriadoEm);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~PedidoNaoEncontradoExceptionTests|FullyQualifiedName~VariacaoNaoEncontradaExceptionTests|FullyQualifiedName~EstoqueInsuficienteExceptionTests|FullyQualifiedName~PedidoEmEstadoFinalExceptionTests|FullyQualifiedName~PedidoServiceTests"`
Expected: 17 passed.

- [ ] **Step 7: Register the service and validator in DI**

Open `src/RedeStore.Api/Program.cs`. Add these `using` statements:

```csharp
using RedeStore.Application.Pedidos;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Pedidos.Validators;
```

Add these registrations right after the `IInscricaoService` registration from Fase 4:

```csharp
builder.Services.AddScoped<IPedidoService, PedidoService>();
builder.Services.AddSingleton<IValidator<CriarPedidoRequest>, CriarPedidoRequestValidator>();
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/RedeStore.Domain/Exceptions/PedidoNaoEncontradoException.cs src/RedeStore.Domain/Exceptions/VariacaoNaoEncontradaException.cs src/RedeStore.Domain/Exceptions/EstoqueInsuficienteException.cs src/RedeStore.Domain/Exceptions/PedidoEmEstadoFinalException.cs src/RedeStore.Application/Pedidos/IPedidoService.cs src/RedeStore.Application/Pedidos/PedidoService.cs src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Domain/PedidoNaoEncontradoExceptionTests.cs tests/RedeStore.UnitTests/Domain/VariacaoNaoEncontradaExceptionTests.cs tests/RedeStore.UnitTests/Domain/EstoqueInsuficienteExceptionTests.cs tests/RedeStore.UnitTests/Domain/PedidoEmEstadoFinalExceptionTests.cs tests/RedeStore.UnitTests/Pedidos/FakePedidoRepository.cs tests/RedeStore.UnitTests/Pedidos/FakeVariacaoRepository.cs tests/RedeStore.UnitTests/Pedidos/PedidoServiceTests.cs
git commit -m "Add PedidoService covering checkout with atomic stock decrement and state machine"
```

---

## Task 4: Map the 4 Endpoints

**Files:**
- Create: `src/RedeStore.Api/Endpoints/PedidosEndpoints.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.IntegrationTests/Pedidos/PedidosEndpointsSmokeTests.cs`

**Interfaces:**
- Consumes: `IPedidoService` (Task 3), `ValidationFilter<T>` (Fase 2), the `ClaimsPrincipal` self-claim extraction pattern already used by `InscricoesEndpoints.MapInscricoesEndpoints` (Fase 4).
- Produces: the 4 pedidos routes wired into the running app. Task 5 exercises all of them end-to-end.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Pedidos/PedidosEndpointsSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Pedidos;

[Collection(IntegrationTestCollection.Name)]
public class PedidosEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PedidosEndpointsSmokeTests(ApiFactory factory)
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

    private async Task<Guid> CriarProdutoComEstoqueAsync(HttpClient clienteAdmin, int estoque)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: $"Camiseta {Guid.NewGuid()}",
            Categoria: "camisetas",
            Preco: 79.90m,
            Descricao: "Camiseta de teste",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: false,
            Variacoes: [new VariacaoRequest("M", "Preto", estoque)]));
        var produto = await response.Content.ReadFromJsonAsync<ProdutoDto>();
        return produto!.Id;
    }

    [Fact]
    public async Task PostPedidos_SemToken_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 1)], "retirada", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostPedidos_ComItemValido_Retorna200EDepoisApareceEmMeusPedidos()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 2)], "retirada", null));
        Assert.Equal(HttpStatusCode.OK, criarResponse.StatusCode);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        meusPedidosResponse.EnsureSuccessStatusCode();
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Single(meusPedidos!);
    }

    [Fact]
    public async Task PostPedidos_ComProdutoInexistente_Retorna404()
    {
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(Guid.NewGuid(), "M", "Preto", 1)], "retirada", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPedidos_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.GetAsync("/pedidos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~PedidosEndpointsSmokeTests"`
Expected: FAIL — routes return 404 (not mapped) instead of the expected status codes.

- [ ] **Step 3: Implement the endpoint group**

Create `src/RedeStore.Api/Endpoints/PedidosEndpoints.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Api.Filters;
using RedeStore.Application.Pedidos;
using RedeStore.Application.Pedidos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class PedidosEndpoints
{
    public static void MapPedidosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/pedidos");

        grupo.MapPost("/", async (CriarPedidoRequest request, ClaimsPrincipal user, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var pedido = await pedidoService.CriarAsync(usuarioId, request, ct);
            return Results.Ok(pedido);
        }).RequireAuthorization().AddEndpointFilter<ValidationFilter<CriarPedidoRequest>>();

        grupo.MapGet("/", async (IPedidoService pedidoService, CancellationToken ct) =>
        {
            var pedidos = await pedidoService.ListarTodosAsync(ct);
            return Results.Ok(pedidos);
        }).RequireAuthorization("Admin");

        grupo.MapPatch("/{id:guid}/avancar-status", async (Guid id, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var pedido = await pedidoService.AvancarStatusAsync(id, ct);
            return Results.Ok(pedido);
        }).RequireAuthorization("Admin");

        app.MapGet("/usuarios/me/pedidos", async (ClaimsPrincipal user, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var pedidos = await pedidoService.ListarPorUsuarioAsync(usuarioId, ct);
            return Results.Ok(pedidos);
        }).RequireAuthorization();
    }
}
```

- [ ] **Step 4: Map the endpoint group in `Program.cs`**

Open `src/RedeStore.Api/Program.cs` and change:

```csharp
app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapProdutosEndpoints();
app.MapEventosEndpoints();
app.MapInscricoesEndpoints();
```

to:

```csharp
app.MapAuthEndpoints();
app.MapUsuariosEndpoints();
app.MapProdutosEndpoints();
app.MapEventosEndpoints();
app.MapInscricoesEndpoints();
app.MapPedidosEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~PedidosEndpointsSmokeTests"`
Expected: 4 passed.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/RedeStore.Api/Endpoints/PedidosEndpoints.cs src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Pedidos/
git commit -m "Map the 4 pedidos endpoints"
```

---

## Task 5: End-to-End Tests Covering the Fase 5 Definition of Done

**Files:**
- Create: `tests/RedeStore.IntegrationTests/Pedidos/PedidoFlowEndToEndTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-4. No new production interfaces are produced by this task — it is the closing verification pass for the phase, matching the design doc's own required coverage (section 7: "Criar pedido com estoque insuficiente → rejeitado, nenhum item parcialmente decrementado").

- [ ] **Step 1: Write the end-to-end tests**

Create `tests/RedeStore.IntegrationTests/Pedidos/PedidoFlowEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Pedidos.Dtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Pedidos;

[Collection(IntegrationTestCollection.Name)]
public class PedidoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public PedidoFlowEndToEndTests(ApiFactory factory)
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

    private async Task<Guid> CriarProdutoComEstoqueAsync(HttpClient clienteAdmin, int estoque)
    {
        var response = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: $"Camiseta {Guid.NewGuid()}",
            Categoria: "camisetas",
            Preco: 79.90m,
            Descricao: "Camiseta de teste",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: false,
            Variacoes: [new VariacaoRequest("M", "Preto", estoque)]));
        var produto = await response.Content.ReadFromJsonAsync<ProdutoDto>();
        return produto!.Id;
    }

    [Fact]
    public async Task FluxoCompleto_CriarListarAvancarStatusAteEstadoFinal_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var criarResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 2)], "retirada", null));
        criarResponse.EnsureSuccessStatusCode();
        var criado = await criarResponse.Content.ReadFromJsonAsync<PedidoDto>();
        Assert.Equal("pago", criado!.Status);
        Assert.Equal(159.80m, criado.ValorTotal);

        var meusPedidosResponse = await clienteJovem.GetAsync("/usuarios/me/pedidos");
        var meusPedidos = await meusPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(meusPedidos!, p => p.Id == criado.Id);

        var todosOsPedidosResponse = await clienteAdmin.GetAsync("/pedidos");
        var todosOsPedidos = await todosOsPedidosResponse.Content.ReadFromJsonAsync<List<PedidoDto>>();
        Assert.Contains(todosOsPedidos!, p => p.Id == criado.Id);

        var avancarUmResponse = await clienteAdmin.PatchAsync($"/pedidos/{criado.Id}/avancar-status", content: null);
        avancarUmResponse.EnsureSuccessStatusCode();
        var apósPrimeiroAvanco = await avancarUmResponse.Content.ReadFromJsonAsync<PedidoDto>();
        Assert.Equal("em_preparo", apósPrimeiroAvanco!.Status);

        var avancarDoisResponse = await clienteAdmin.PatchAsync($"/pedidos/{criado.Id}/avancar-status", content: null);
        avancarDoisResponse.EnsureSuccessStatusCode();
        var apósSegundoAvanco = await avancarDoisResponse.Content.ReadFromJsonAsync<PedidoDto>();
        Assert.Equal("retirado", apósSegundoAvanco!.Status);

        var avancarTresResponse = await clienteAdmin.PatchAsync($"/pedidos/{criado.Id}/avancar-status", content: null);
        Assert.Equal(HttpStatusCode.Conflict, avancarTresResponse.StatusCode);
    }

    [Fact]
    public async Task PatchAvancarStatus_ComTokenDeUsuarioNaoAdmin_Retorna403()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();
        var criarResponse = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "retirada", null));
        var criado = await criarResponse.Content.ReadFromJsonAsync<PedidoDto>();

        var response = await clienteJovem.PatchAsync($"/pedidos/{criado!.Id}/avancar-status", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostPedidos_ComEntregaSemEndereco_Retorna400()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "entrega", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CriarPedido_ComEstoqueInsuficienteEmUmDosItens_RejeitaENaoDecrementaNenhumItem()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoComEstoqueId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 10);
        var produtoSemEstoqueId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 1);
        using var clienteJovem = await CriarClienteJovemAsync();

        var response = await clienteJovem.PostAsJsonAsync("/pedidos", new CriarPedidoRequest(
            [
                new ItemPedidoRequest(produtoComEstoqueId, "M", "Preto", 2),
                new ItemPedidoRequest(produtoSemEstoqueId, "M", "Preto", 5),
            ],
            "retirada", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var produtoResponse = await _client.GetAsync($"/produtos/{produtoComEstoqueId}");
        var produto = await produtoResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(10, produto!.Variacoes[0].Estoque);
    }

    [Fact]
    public async Task DoisCheckoutsSimultaneosNaUltimaUnidadeDeEstoque_ApenasUmSucede()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var produtoId = await CriarProdutoComEstoqueAsync(clienteAdmin, estoque: 1);
        using var primeiroUsuario = await CriarClienteJovemAsync();
        using var segundoUsuario = await CriarClienteJovemAsync();
        var request = new CriarPedidoRequest([new ItemPedidoRequest(produtoId, "M", "Preto", 1)], "retirada", null);

        var tarefaUm = primeiroUsuario.PostAsJsonAsync("/pedidos", request);
        var tarefaDois = segundoUsuario.PostAsJsonAsync("/pedidos", request);
        await Task.WhenAll(tarefaUm, tarefaDois);

        var statusCodes = new[] { (await tarefaUm).StatusCode, (await tarefaDois).StatusCode };
        Assert.Single(statusCodes, s => s == HttpStatusCode.OK);
        Assert.Single(statusCodes, s => s == HttpStatusCode.Conflict);

        var produtoResponse = await _client.GetAsync($"/produtos/{produtoId}");
        var produto = await produtoResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(0, produto!.Variacoes[0].Estoque);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (if anything from Tasks 1-4 was missed, this is where it surfaces)**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~PedidoFlowEndToEndTests"`
Expected: all of Tasks 1-4 are already implemented at this point, so this should already pass; if it doesn't, fix the gap it surfaces before proceeding (do not edit these tests to work around a production bug).

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter "FullyQualifiedName~PedidoFlowEndToEndTests"`
Expected: 5 passed. `DoisCheckoutsSimultaneosNaUltimaUnidadeDeEstoque_ApenasUmSucede` is the one that actually proves the atomic conditional `UPDATE` prevents overselling — it runs against the real Testcontainers Postgres with two genuinely parallel HTTP requests, which the in-memory fakes from Task 3's unit tests cannot exercise.

- [ ] **Step 4: Run the entire solution's test suite one final time**

Run: `dotnet test`
Expected: full solution green — this is the Fase 5 Definition of Done: checkout monta o snapshot a partir do produto atual (nunca do client), decremento de estoque atômico sem overselling mesmo sob concorrência real, pedido com estoque insuficiente em qualquer item é rejeitado por inteiro (nenhum item parcialmente decrementado), endereço obrigatório só quando `formaEntrega == 'entrega'`, máquina de estados exata (`pago → em_preparo → retirado/entregue`) com rejeição em estado final, e a autorização por papel bate com a seção 8 do doc de requisitos.

- [ ] **Step 5: Commit**

```bash
git add tests/RedeStore.IntegrationTests/Pedidos/PedidoFlowEndToEndTests.cs
git commit -m "Add end-to-end tests covering the Fase 5 Definition of Done"
```
