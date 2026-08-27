# Backend REDE — Fase 3 (Produtos) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Produtos subsystem — public catalog browsing (list with filters, destaques, detail) plus admin CRUD — on top of the Fase 1/Fase 2 foundation, with `Produto`/`Variacao` as a real relational parent/child table pair (not JSON), ready for the atomic stock decrement Fase 5 will add later.

**Architecture:** New `Produto`/`Variacao` entities and their EF Core migration; a `IProdutoRepository` interface in `Application.Common` (implementation in `Infrastructure.Persistence.Repositories`, per the fixed dependency graph); a single `ProdutoService` covering all product use cases, always recalculating `Tamanhos`/`Cores` from `Variacoes` on write; the existing generic `ValidationFilter<T>` (Fase 2) reused for the two new request DTOs; a new `ProdutoNaoEncontradoException` following the exact `DomainException` shape from Fase 2.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql (already wired), FluentValidation (already wired), `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`, already wired), Testcontainers.PostgreSql (already wired). No new NuGet packages needed for this phase.

**Spec:** `docs/2026-08-26-rede-backend-design.md` (sections 3, 7, 9 — "Fase 3 — Produtos") and `docs/2026-08-26-rede-backend-requisitos.md` (section 1.2 "Produto", section 3 "Produtos (Loja + Admin)", section 8 "Resumo de autorização por papel").

## Global Constraints

- Target framework `net10.0` for every project (already set).
- Fixed dependency graph from Fase 1/2 (unchanged): `Domain` → nothing; `Application` → `Domain` only; `Infrastructure` → `Application` + `Domain`; `Api` → `Application` + `Infrastructure`. Repository **interfaces** live in `RedeStore.Application.Common`; **implementations** live in `RedeStore.Infrastructure.Persistence.Repositories`. `Application` must never reference `Infrastructure` or EF Core types directly.
- `Produto.Categoria` is a closed 3-value enum (`Camisetas`, `Moletons`, `Acessorios`) that maps 1:1 to the lowercase wire strings `"camisetas"`/`"moletons"`/`"acessorios"` via `categoria.ToString().ToLowerInvariant()` — the exact same convention `Papel` already uses in Fase 2. This mapping is used everywhere `CategoriaProduto` crosses into a DTO.
- `Produto.Tamanhos`/`Produto.Cores` are persisted columns but are **always recalculated from `Produto.Variacoes`** at create/update time in `ProdutoService` — never trusted verbatim from client input, per the design doc's decision (section 3).
- `Produto.Variacoes` is a real relational child table (`Variacoes`, FK `ProdutoId`) — not a JSON column — so a future atomic `UPDATE ... WHERE "Estoque" >= :qtd` (Fase 5) can operate row-by-row.
- No mocking library. Unit tests needing a fake repository use a small hand-rolled in-memory class implementing the same interface — not Moq/NSubstitute (same rule as Fase 2).
- `DELETE /produtos/:id` is a hard delete (decision already registered in the requirements doc, section 3) — historic orders will snapshot product fields at the `ItemPedido` level in Fase 5, so a deleted product does not need to remain queryable.
- New `RedeStore.Domain.Exceptions.ProdutoNaoEncontradoException` only overrides `Codigo` (`"PRODUTO_NAO_ENCONTRADO"`) / `StatusCode` (404) and takes a `message` — no extra logic, matching the Fase 2 `DomainException` subclass pattern exactly. `GlobalExceptionHandler` needs no changes (it already handles any `DomainException` polymorphically).
- Write endpoints (`POST`/`PATCH`/`DELETE /produtos`) require the `"Admin"` authorization policy already registered in `Program.cs` (`RequireAuthorization("Admin")`); read endpoints (`GET /produtos`, `GET /produtos/destaques`, `GET /produtos/:id`) stay public — per the authorization table in the requirements doc (section 8).
- Shared testing convention (same as Fase 2): integration tests share one Postgres container/database across the whole run via `ICollectionFixture`, so tests must not collide — use randomly-suffixed unique values (e.g. product names with a `Guid` in them) wherever a test asserts on a specific row showing up in a list/search result.
- When `ProdutoService.AtualizarAsync` replaces `Variacoes`, it must mutate the already-tracked `Produto.Variacoes` list in place (`.Clear()` then `.Add(...)`) rather than assigning a new `List<Variacao>` — this is required for EF Core's orphan-deletion behavior (required FK relationship) to actually delete the replaced rows instead of leaving them dangling.

---

## Task 1: Produto & Variacao Persistence

**Files:**
- Create: `src/RedeStore.Domain/Entities/CategoriaProduto.cs`
- Create: `src/RedeStore.Domain/Entities/Produto.cs`
- Create: `src/RedeStore.Domain/Entities/Variacao.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/ProdutoConfiguration.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Configurations/VariacaoConfiguration.cs`
- Modify: `src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs`
- Create: `src/RedeStore.Application/Common/IProdutoRepository.cs`
- Create: `src/RedeStore.Infrastructure/Persistence/Repositories/ProdutoRepository.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Migration: generated under `src/RedeStore.Infrastructure/Persistence/Migrations/`
- Test: `tests/RedeStore.IntegrationTests/Persistence/ProdutoRepositoryTests.cs`

**Interfaces:**
- Consumes: `ApiFactory`/`IntegrationTestCollection` (Fase 2, unchanged).
- Produces: `RedeStore.Application.Common.IProdutoRepository` — `Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct)`, `Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct)`, `Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct)`, `Task AdicionarAsync(Produto produto, CancellationToken ct)`, `Task AtualizarAsync(Produto produto, CancellationToken ct)`, `Task RemoverAsync(Produto produto, CancellationToken ct)`. Registered as `Scoped` in DI. Task 3 depends on these exact signatures.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Persistence/ProdutoRepositoryTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Persistence;

[Collection(IntegrationTestCollection.Name)]
public class ProdutoRepositoryTests
{
    private readonly ApiFactory _factory;

    public ProdutoRepositoryTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static Produto CriarProdutoDeTeste(string? nome = null) => new()
    {
        Id = Guid.NewGuid(),
        Nome = nome ?? $"Camiseta {Guid.NewGuid()}",
        Categoria = CategoriaProduto.Camisetas,
        Preco = 79.90m,
        Descricao = "Camiseta oficial da Rede",
        Fotos = ["https://exemplo.com/foto1.jpg"],
        Tamanhos = ["M"],
        Cores = ["Preto"],
        Destaque = false,
        Variacoes = [new Variacao { Id = Guid.NewGuid(), Tamanho = "M", Cor = "Preto", Estoque = 10 }],
    };

    [Fact]
    public async Task AdicionarAsync_ThenBuscarPorIdAsync_RetornaProdutoComVariacoes()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var produto = CriarProdutoDeTeste();

        await repositorio.AdicionarAsync(produto, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(produto.Id, CancellationToken.None);

        Assert.NotNull(encontrado);
        Assert.Equal(produto.Nome, encontrado!.Nome);
        Assert.Single(encontrado.Variacoes);
        Assert.Equal("M", encontrado.Variacoes[0].Tamanho);
    }

    [Fact]
    public async Task BuscarPorIdAsync_ComIdInexistente_RetornaNull()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();

        var encontrado = await repositorio.BuscarPorIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task ListarAsync_ComFiltroDeCategoria_RetornaSoDaCategoria()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var camiseta = CriarProdutoDeTeste();
        var moletom = CriarProdutoDeTeste();
        moletom.Categoria = CategoriaProduto.Moletons;
        moletom.Variacoes = [new Variacao { Id = Guid.NewGuid(), Tamanho = "G", Cor = "Cinza", Estoque = 5 }];
        await repositorio.AdicionarAsync(camiseta, CancellationToken.None);
        await repositorio.AdicionarAsync(moletom, CancellationToken.None);

        var resultado = await repositorio.ListarAsync(CategoriaProduto.Moletons, null, CancellationToken.None);

        Assert.Contains(resultado, p => p.Id == moletom.Id);
        Assert.DoesNotContain(resultado, p => p.Id == camiseta.Id);
    }

    [Fact]
    public async Task ListarAsync_ComBusca_RetornaSoOsQueContemOTermoNoNomeCaseInsensitive()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var produto = CriarProdutoDeTeste($"Camiseta Especial {Guid.NewGuid()}");
        await repositorio.AdicionarAsync(produto, CancellationToken.None);

        var resultado = await repositorio.ListarAsync(null, "especial", CancellationToken.None);

        Assert.Contains(resultado, p => p.Id == produto.Id);
    }

    [Fact]
    public async Task ListarDestaquesAsync_RetornaSoOsProdutosEmDestaque()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var destaque = CriarProdutoDeTeste();
        destaque.Destaque = true;
        var comum = CriarProdutoDeTeste();
        comum.Destaque = false;
        await repositorio.AdicionarAsync(destaque, CancellationToken.None);
        await repositorio.AdicionarAsync(comum, CancellationToken.None);

        var resultado = await repositorio.ListarDestaquesAsync(CancellationToken.None);

        Assert.Contains(resultado, p => p.Id == destaque.Id);
        Assert.DoesNotContain(resultado, p => p.Id == comum.Id);
    }

    [Fact]
    public async Task AtualizarAsync_SubstituindoVariacoes_RemoveAsAntigasEPersisteAsNovas()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var produto = CriarProdutoDeTeste();
        await repositorio.AdicionarAsync(produto, CancellationToken.None);

        var carregado = await repositorio.BuscarPorIdAsync(produto.Id, CancellationToken.None);
        carregado!.Variacoes.Clear();
        carregado.Variacoes.Add(new Variacao { Id = Guid.NewGuid(), Tamanho = "G", Cor = "Azul", Estoque = 3 });
        await repositorio.AtualizarAsync(carregado, CancellationToken.None);

        var recarregado = await repositorio.BuscarPorIdAsync(produto.Id, CancellationToken.None);
        Assert.Single(recarregado!.Variacoes);
        Assert.Equal("G", recarregado.Variacoes[0].Tamanho);
    }

    [Fact]
    public async Task RemoverAsync_ExcluiOProdutoESuasVariacoes()
    {
        using var scope = _factory.Services.CreateScope();
        var repositorio = scope.ServiceProvider.GetRequiredService<IProdutoRepository>();
        var produto = CriarProdutoDeTeste();
        await repositorio.AdicionarAsync(produto, CancellationToken.None);

        await repositorio.RemoverAsync(produto, CancellationToken.None);
        var encontrado = await repositorio.BuscarPorIdAsync(produto.Id, CancellationToken.None);

        Assert.Null(encontrado);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~ProdutoRepositoryTests`
Expected: FAIL to compile — none of the entities/interfaces/repository exist yet.

- [ ] **Step 3: Create the entities**

Create `src/RedeStore.Domain/Entities/CategoriaProduto.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public enum CategoriaProduto
{
    Camisetas,
    Moletons,
    Acessorios,
}
```

Create `src/RedeStore.Domain/Entities/Produto.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Produto
{
    public Guid Id { get; set; }
    public required string Nome { get; set; }
    public CategoriaProduto Categoria { get; set; }
    public decimal Preco { get; set; }
    public required string Descricao { get; set; }
    public List<string> Fotos { get; set; } = [];
    public List<string> Tamanhos { get; set; } = [];
    public List<string> Cores { get; set; } = [];
    public bool Destaque { get; set; }
    public List<Variacao> Variacoes { get; set; } = [];
}
```

Create `src/RedeStore.Domain/Entities/Variacao.cs`:

```csharp
namespace RedeStore.Domain.Entities;

public class Variacao
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public required string Tamanho { get; set; }
    public required string Cor { get; set; }
    public int Estoque { get; set; }
}
```

- [ ] **Step 4: Configure the entities and wire them into `RedeStoreDbContext`**

Create `src/RedeStore.Infrastructure/Persistence/Configurations/ProdutoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("Produtos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Nome).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Categoria).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Preco).HasPrecision(10, 2);
        builder.Property(p => p.Descricao).IsRequired();
        builder.Property(p => p.Fotos).IsRequired();
        builder.Property(p => p.Tamanhos).IsRequired();
        builder.Property(p => p.Cores).IsRequired();
        builder.HasMany(p => p.Variacoes).WithOne().HasForeignKey(v => v.ProdutoId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

Create `src/RedeStore.Infrastructure/Persistence/Configurations/VariacaoConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Configurations;

public class VariacaoConfiguration : IEntityTypeConfiguration<Variacao>
{
    public void Configure(EntityTypeBuilder<Variacao> builder)
    {
        builder.ToTable("Variacoes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Tamanho).IsRequired().HasMaxLength(50);
        builder.Property(v => v.Cor).IsRequired().HasMaxLength(50);
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RedeStoreDbContext).Assembly);
    }
}
```

- [ ] **Step 5: Create the repository interface and implementation**

Create `src/RedeStore.Application/Common/IProdutoRepository.cs`:

```csharp
using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IProdutoRepository
{
    Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct);
    Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct);
    Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Produto produto, CancellationToken ct);
    Task AtualizarAsync(Produto produto, CancellationToken ct);
    Task RemoverAsync(Produto produto, CancellationToken ct);
}
```

Create `src/RedeStore.Infrastructure/Persistence/Repositories/ProdutoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class ProdutoRepository : IProdutoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public ProdutoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct)
    {
        var query = _dbContext.Produtos.Include(p => p.Variacoes).AsQueryable();

        if (categoria is not null)
        {
            query = query.Where(p => p.Categoria == categoria);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            query = query.Where(p => EF.Functions.ILike(p.Nome, $"%{busca}%"));
        }

        return await query.ToListAsync(ct);
    }

    public Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct) =>
        _dbContext.Produtos.Include(p => p.Variacoes).Where(p => p.Destaque).ToListAsync(ct);

    public Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Produtos.Include(p => p.Variacoes).SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AdicionarAsync(Produto produto, CancellationToken ct)
    {
        _dbContext.Produtos.Add(produto);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Produto produto, CancellationToken ct)
    {
        _dbContext.Produtos.Update(produto);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(Produto produto, CancellationToken ct)
    {
        _dbContext.Produtos.Remove(produto);
        await _dbContext.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Register the repository in DI**

Open `src/RedeStore.Api/Program.cs` and add this line right after the `IPasswordResetTokenRepository` registration (keep everything else in the file unchanged):

```csharp
builder.Services.AddScoped<IProdutoRepository, ProdutoRepository>();
```

No new `using` statements are needed — `RedeStore.Application.Common` and `RedeStore.Infrastructure.Persistence.Repositories` are already imported.

- [ ] **Step 7: Create the migration**

```bash
dotnet ef migrations add AdicionarProdutos --project src/RedeStore.Infrastructure --startup-project src/RedeStore.Api --output-dir Persistence/Migrations
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~ProdutoRepositoryTests`
Expected: 7 passed. (`ApiFactory.InitializeAsync` applies the new `AdicionarProdutos` migration automatically.)

- [ ] **Step 9: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 10: Commit**

```bash
git add src/RedeStore.Domain/Entities/CategoriaProduto.cs src/RedeStore.Domain/Entities/Produto.cs src/RedeStore.Domain/Entities/Variacao.cs src/RedeStore.Infrastructure/Persistence/Configurations/ProdutoConfiguration.cs src/RedeStore.Infrastructure/Persistence/Configurations/VariacaoConfiguration.cs src/RedeStore.Infrastructure/Persistence/RedeStoreDbContext.cs src/RedeStore.Application/Common/IProdutoRepository.cs src/RedeStore.Infrastructure/Persistence/Repositories/ProdutoRepository.cs src/RedeStore.Infrastructure/Persistence/Migrations/ src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Persistence/ProdutoRepositoryTests.cs
git commit -m "Add Produto and Variacao persistence with EF Core migration"
```

---

## Task 2: DTOs and FluentValidation Validators

**Files:**
- Create: `src/RedeStore.Application/Produtos/Dtos/VariacaoRequest.cs`
- Create: `src/RedeStore.Application/Produtos/Dtos/VariacaoDto.cs`
- Create: `src/RedeStore.Application/Produtos/Dtos/CriarProdutoRequest.cs`
- Create: `src/RedeStore.Application/Produtos/Dtos/AtualizarProdutoRequest.cs`
- Create: `src/RedeStore.Application/Produtos/Dtos/ProdutoDto.cs`
- Create: `src/RedeStore.Application/Produtos/Validators/CriarProdutoRequestValidator.cs`
- Create: `src/RedeStore.Application/Produtos/Validators/AtualizarProdutoRequestValidator.cs`
- Test: `tests/RedeStore.UnitTests/Produtos/Validators/CriarProdutoRequestValidatorTests.cs`
- Test: `tests/RedeStore.UnitTests/Produtos/Validators/AtualizarProdutoRequestValidatorTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 directly (DTOs are plain data shapes).
- Produces: `CriarProdutoRequest(string Nome, string Categoria, decimal Preco, string Descricao, List<string>? Fotos, bool Destaque, List<VariacaoRequest> Variacoes)`, `AtualizarProdutoRequest(string? Nome, string? Categoria, decimal? Preco, string? Descricao, List<string>? Fotos, bool? Destaque, List<VariacaoRequest>? Variacoes)`, `VariacaoRequest(string Tamanho, string Cor, int Estoque)`, `ProdutoDto(Guid Id, string Nome, string Categoria, decimal Preco, string Descricao, List<string> Fotos, List<string> Tamanhos, List<string> Cores, bool Destaque, List<VariacaoDto> Variacoes)`, `VariacaoDto(Guid Id, string Tamanho, string Cor, int Estoque)`. `Categoria` on every DTO is the lowercase string, never the enum. Task 3 and Task 4 depend on these exact shapes; Task 4 wires `CriarProdutoRequestValidator`/`AtualizarProdutoRequestValidator` into `ValidationFilter<T>` (Fase 2).

- [ ] **Step 1: Write the failing validator tests**

Create `tests/RedeStore.UnitTests/Produtos/Validators/CriarProdutoRequestValidatorTests.cs`:

```csharp
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Application.Produtos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Produtos.Validators;

public class CriarProdutoRequestValidatorTests
{
    private readonly CriarProdutoRequestValidator _validator = new();

    private static CriarProdutoRequest RequestValido() => new(
        Nome: "Camiseta Rede",
        Categoria: "camisetas",
        Preco: 79.90m,
        Descricao: "Camiseta oficial",
        Fotos: ["https://exemplo.com/foto.jpg"],
        Destaque: false,
        Variacoes: [new VariacaoRequest("M", "Preto", 10)]);

    [Fact]
    public void Validate_ComRequestValido_NaoRetornaErros()
    {
        var resultado = _validator.Validate(RequestValido());

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComCategoriaInvalida_RetornaErro()
    {
        var request = RequestValido() with { Categoria = "sapatos" };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoZero_RetornaErro()
    {
        var request = RequestValido() with { Preco = 0 };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_SemVariacoes_RetornaErro()
    {
        var request = RequestValido() with { Variacoes = [] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComVariacaoComEstoqueNegativo_RetornaErro()
    {
        var request = RequestValido() with { Variacoes = [new VariacaoRequest("M", "Preto", -1)] };

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
```

Create `tests/RedeStore.UnitTests/Produtos/Validators/AtualizarProdutoRequestValidatorTests.cs`:

```csharp
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Application.Produtos.Validators;
using Xunit;

namespace RedeStore.UnitTests.Produtos.Validators;

public class AtualizarProdutoRequestValidatorTests
{
    private readonly AtualizarProdutoRequestValidator _validator = new();

    [Fact]
    public void Validate_ComTodosOsCamposNulos_NaoRetornaErros()
    {
        var request = new AtualizarProdutoRequest(null, null, null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.True(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComCategoriaInvalida_RetornaErro()
    {
        var request = new AtualizarProdutoRequest(null, "sapatos", null, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComPrecoZero_RetornaErro()
    {
        var request = new AtualizarProdutoRequest(null, null, 0m, null, null, null, null);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }

    [Fact]
    public void Validate_ComListaDeVariacoesVazia_RetornaErro()
    {
        var request = new AtualizarProdutoRequest(null, null, null, null, null, null, []);

        var resultado = _validator.Validate(request);

        Assert.False(resultado.IsValid);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~CriarProdutoRequestValidatorTests|FullyQualifiedName~AtualizarProdutoRequestValidatorTests"`
Expected: FAIL to compile — none of the DTOs/validators exist yet.

- [ ] **Step 3: Create the DTOs**

Create `src/RedeStore.Application/Produtos/Dtos/VariacaoRequest.cs`:

```csharp
namespace RedeStore.Application.Produtos.Dtos;

public sealed record VariacaoRequest(string Tamanho, string Cor, int Estoque);
```

Create `src/RedeStore.Application/Produtos/Dtos/VariacaoDto.cs`:

```csharp
namespace RedeStore.Application.Produtos.Dtos;

public sealed record VariacaoDto(Guid Id, string Tamanho, string Cor, int Estoque);
```

Create `src/RedeStore.Application/Produtos/Dtos/CriarProdutoRequest.cs`:

```csharp
namespace RedeStore.Application.Produtos.Dtos;

public sealed record CriarProdutoRequest(
    string Nome,
    string Categoria,
    decimal Preco,
    string Descricao,
    List<string>? Fotos,
    bool Destaque,
    List<VariacaoRequest> Variacoes);
```

Create `src/RedeStore.Application/Produtos/Dtos/AtualizarProdutoRequest.cs`:

```csharp
namespace RedeStore.Application.Produtos.Dtos;

public sealed record AtualizarProdutoRequest(
    string? Nome,
    string? Categoria,
    decimal? Preco,
    string? Descricao,
    List<string>? Fotos,
    bool? Destaque,
    List<VariacaoRequest>? Variacoes);
```

Create `src/RedeStore.Application/Produtos/Dtos/ProdutoDto.cs`:

```csharp
namespace RedeStore.Application.Produtos.Dtos;

public sealed record ProdutoDto(
    Guid Id,
    string Nome,
    string Categoria,
    decimal Preco,
    string Descricao,
    List<string> Fotos,
    List<string> Tamanhos,
    List<string> Cores,
    bool Destaque,
    List<VariacaoDto> Variacoes);
```

- [ ] **Step 4: Implement the validators**

Create `src/RedeStore.Application/Produtos/Validators/CriarProdutoRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos.Validators;

public sealed class CriarProdutoRequestValidator : AbstractValidator<CriarProdutoRequest>
{
    private static readonly string[] CategoriasValidas = ["camisetas", "moletons", "acessorios"];

    public CriarProdutoRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty();
        RuleFor(r => r.Categoria)
            .Must(c => CategoriasValidas.Contains(c))
            .WithMessage("Categoria deve ser 'camisetas', 'moletons' ou 'acessorios'.");
        RuleFor(r => r.Preco).GreaterThan(0);
        RuleFor(r => r.Descricao).NotEmpty();
        RuleFor(r => r.Variacoes)
            .Must(v => v is not null && v.Count > 0)
            .WithMessage("O produto precisa de ao menos 1 variação.");
        RuleForEach(r => r.Variacoes).ChildRules(variacao =>
        {
            variacao.RuleFor(v => v.Tamanho).NotEmpty();
            variacao.RuleFor(v => v.Cor).NotEmpty();
            variacao.RuleFor(v => v.Estoque).GreaterThanOrEqualTo(0);
        });
    }
}
```

Create `src/RedeStore.Application/Produtos/Validators/AtualizarProdutoRequestValidator.cs`:

```csharp
using FluentValidation;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos.Validators;

public sealed class AtualizarProdutoRequestValidator : AbstractValidator<AtualizarProdutoRequest>
{
    private static readonly string[] CategoriasValidas = ["camisetas", "moletons", "acessorios"];

    public AtualizarProdutoRequestValidator()
    {
        RuleFor(r => r.Nome).NotEmpty().When(r => r.Nome is not null);
        RuleFor(r => r.Categoria)
            .Must(c => CategoriasValidas.Contains(c))
            .When(r => r.Categoria is not null)
            .WithMessage("Categoria deve ser 'camisetas', 'moletons' ou 'acessorios'.");
        RuleFor(r => r.Preco).GreaterThan(0).When(r => r.Preco.HasValue);
        RuleFor(r => r.Descricao).NotEmpty().When(r => r.Descricao is not null);
        RuleFor(r => r.Variacoes)
            .Must(v => v!.Count > 0)
            .When(r => r.Variacoes is not null)
            .WithMessage("O produto precisa de ao menos 1 variação.");
        RuleForEach(r => r.Variacoes).ChildRules(variacao =>
        {
            variacao.RuleFor(v => v.Tamanho).NotEmpty();
            variacao.RuleFor(v => v.Cor).NotEmpty();
            variacao.RuleFor(v => v.Estoque).GreaterThanOrEqualTo(0);
        }).When(r => r.Variacoes is not null);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~CriarProdutoRequestValidatorTests|FullyQualifiedName~AtualizarProdutoRequestValidatorTests"`
Expected: 9 passed.

- [ ] **Step 6: Commit**

```bash
git add src/RedeStore.Application/Produtos/Dtos/ src/RedeStore.Application/Produtos/Validators/ tests/RedeStore.UnitTests/Produtos/Validators/
git commit -m "Add Produto DTOs and FluentValidation validators"
```

---

## Task 3: ProdutoService

**Files:**
- Create: `src/RedeStore.Domain/Exceptions/ProdutoNaoEncontradoException.cs`
- Create: `src/RedeStore.Application/Produtos/IProdutoService.cs`
- Create: `src/RedeStore.Application/Produtos/ProdutoService.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.UnitTests/Domain/ProdutoNaoEncontradoExceptionTests.cs`
- Test: `tests/RedeStore.UnitTests/Produtos/FakeProdutoRepository.cs`
- Test: `tests/RedeStore.UnitTests/Produtos/ProdutoServiceTests.cs`

**Interfaces:**
- Consumes: `IProdutoRepository` (Task 1), the DTOs from Task 2.
- Produces: `RedeStore.Application.Produtos.IProdutoService` — `Task<List<ProdutoDto>> ListarAsync(string? categoria, string? busca, CancellationToken ct)`, `Task<List<ProdutoDto>> ListarDestaquesAsync(CancellationToken ct)`, `Task<ProdutoDto> ObterPorIdAsync(Guid id, CancellationToken ct)`, `Task<ProdutoDto> CriarAsync(CriarProdutoRequest request, CancellationToken ct)`, `Task<ProdutoDto> AtualizarAsync(Guid id, AtualizarProdutoRequest request, CancellationToken ct)`, `Task RemoverAsync(Guid id, CancellationToken ct)`. `RedeStore.Domain.Exceptions.ProdutoNaoEncontradoException` (`"PRODUTO_NAO_ENCONTRADO"`, 404). Task 4 maps all `IProdutoService` methods to HTTP endpoints and relies on `ProdutoNaoEncontradoException` producing a 404 via the existing `GlobalExceptionHandler`.

- [ ] **Step 1: Write the failing exception test**

Create `tests/RedeStore.UnitTests/Domain/ProdutoNaoEncontradoExceptionTests.cs`:

```csharp
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Domain;

public class ProdutoNaoEncontradoExceptionTests
{
    [Fact]
    public void ProdutoNaoEncontradoException_TemCodigoEStatusCorretos()
    {
        var exception = new ProdutoNaoEncontradoException("produto não encontrado");

        Assert.Equal("PRODUTO_NAO_ENCONTRADO", exception.Codigo);
        Assert.Equal(404, exception.StatusCode);
    }
}
```

- [ ] **Step 2: Write the failing service tests**

Create `tests/RedeStore.UnitTests/Produtos/FakeProdutoRepository.cs` (an in-memory test double, not a mock — Global Constraints forbid mocking libraries):

```csharp
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Produtos;

public sealed class FakeProdutoRepository : IProdutoRepository
{
    private readonly Dictionary<Guid, Produto> _produtosPorId = new();

    public Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct)
    {
        var query = _produtosPorId.Values.AsEnumerable();

        if (categoria is not null)
        {
            query = query.Where(p => p.Categoria == categoria);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            query = query.Where(p => p.Nome.Contains(busca, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(query.ToList());
    }

    public Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct) =>
        Task.FromResult(_produtosPorId.Values.Where(p => p.Destaque).ToList());

    public Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_produtosPorId.GetValueOrDefault(id));

    public Task AdicionarAsync(Produto produto, CancellationToken ct)
    {
        _produtosPorId[produto.Id] = produto;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Produto produto, CancellationToken ct)
    {
        _produtosPorId[produto.Id] = produto;
        return Task.CompletedTask;
    }

    public Task RemoverAsync(Produto produto, CancellationToken ct)
    {
        _produtosPorId.Remove(produto.Id);
        return Task.CompletedTask;
    }
}
```

Create `tests/RedeStore.UnitTests/Produtos/ProdutoServiceTests.cs`:

```csharp
using RedeStore.Application.Produtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Exceptions;
using Xunit;

namespace RedeStore.UnitTests.Produtos;

public class ProdutoServiceTests
{
    private readonly FakeProdutoRepository _repositorio = new();
    private readonly ProdutoService _sut;

    public ProdutoServiceTests()
    {
        _sut = new ProdutoService(_repositorio);
    }

    private static CriarProdutoRequest RequestValido(string? nome = null) => new(
        Nome: nome ?? "Camiseta Rede",
        Categoria: "camisetas",
        Preco: 79.90m,
        Descricao: "Camiseta oficial",
        Fotos: ["https://exemplo.com/foto.jpg"],
        Destaque: false,
        Variacoes: [new VariacaoRequest("M", "Preto", 10), new VariacaoRequest("G", "Preto", 5)]);

    [Fact]
    public async Task CriarAsync_RecalculaTamanhosECoresAPartirDasVariacoes()
    {
        var resposta = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        Assert.Equal(["M", "G"], resposta.Tamanhos);
        Assert.Equal(["Preto"], resposta.Cores);
        Assert.Equal("camisetas", resposta.Categoria);
    }

    [Fact]
    public async Task ObterPorIdAsync_ComIdInexistente_LancaProdutoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<ProdutoNaoEncontradoException>(() =>
            _sut.ObterPorIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ObterPorIdAsync_ComIdExistente_RetornaOProduto()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        var encontrado = await _sut.ObterPorIdAsync(criado.Id, CancellationToken.None);

        Assert.Equal(criado.Id, encontrado.Id);
    }

    [Fact]
    public async Task AtualizarAsync_ComIdInexistente_LancaProdutoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<ProdutoNaoEncontradoException>(() =>
            _sut.AtualizarAsync(Guid.NewGuid(), new AtualizarProdutoRequest(null, null, null, null, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task AtualizarAsync_SubstituindoVariacoes_RecalculaTamanhosECores()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        var atualizado = await _sut.AtualizarAsync(
            criado.Id,
            new AtualizarProdutoRequest(null, null, null, null, null, null, [new VariacaoRequest("U", "Azul", 3)]),
            CancellationToken.None);

        Assert.Equal(["U"], atualizado.Tamanhos);
        Assert.Equal(["Azul"], atualizado.Cores);
        Assert.Single(atualizado.Variacoes);
    }

    [Fact]
    public async Task AtualizarAsync_ComApenasNome_MantemAsVariacoesOriginais()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        var atualizado = await _sut.AtualizarAsync(
            criado.Id,
            new AtualizarProdutoRequest("Novo Nome", null, null, null, null, null, null),
            CancellationToken.None);

        Assert.Equal("Novo Nome", atualizado.Nome);
        Assert.Equal(2, atualizado.Variacoes.Count);
    }

    [Fact]
    public async Task RemoverAsync_ComIdInexistente_LancaProdutoNaoEncontradoException()
    {
        await Assert.ThrowsAsync<ProdutoNaoEncontradoException>(() =>
            _sut.RemoverAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RemoverAsync_ComIdExistente_RemoveOProduto()
    {
        var criado = await _sut.CriarAsync(RequestValido(), CancellationToken.None);

        await _sut.RemoverAsync(criado.Id, CancellationToken.None);

        await Assert.ThrowsAsync<ProdutoNaoEncontradoException>(() =>
            _sut.ObterPorIdAsync(criado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListarAsync_ComFiltroDeCategoria_RetornaSoDaCategoria()
    {
        await _sut.CriarAsync(RequestValido("Camiseta A"), CancellationToken.None);
        var moletom = RequestValido("Moletom B") with { Categoria = "moletons" };
        await _sut.CriarAsync(moletom, CancellationToken.None);

        var resultado = await _sut.ListarAsync("moletons", null, CancellationToken.None);

        Assert.Single(resultado);
        Assert.Equal("Moletom B", resultado[0].Nome);
    }

    [Fact]
    public async Task ListarDestaquesAsync_RetornaSoOsProdutosEmDestaque()
    {
        await _sut.CriarAsync(RequestValido("Comum"), CancellationToken.None);
        var destaque = RequestValido("Em Destaque") with { Destaque = true };
        await _sut.CriarAsync(destaque, CancellationToken.None);

        var resultado = await _sut.ListarDestaquesAsync(CancellationToken.None);

        Assert.Single(resultado);
        Assert.Equal("Em Destaque", resultado[0].Nome);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~ProdutoNaoEncontradoExceptionTests|FullyQualifiedName~ProdutoServiceTests"`
Expected: FAIL to compile — `ProdutoNaoEncontradoException`, `IProdutoService`, `ProdutoService` don't exist yet.

- [ ] **Step 4: Implement the exception**

Create `src/RedeStore.Domain/Exceptions/ProdutoNaoEncontradoException.cs`:

```csharp
namespace RedeStore.Domain.Exceptions;

public sealed class ProdutoNaoEncontradoException : DomainException
{
    public override string Codigo => "PRODUTO_NAO_ENCONTRADO";
    public override int StatusCode => 404;

    public ProdutoNaoEncontradoException(string message) : base(message)
    {
    }
}
```

- [ ] **Step 5: Implement `IProdutoService` and `ProdutoService`**

Create `src/RedeStore.Application/Produtos/IProdutoService.cs`:

```csharp
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos;

public interface IProdutoService
{
    Task<List<ProdutoDto>> ListarAsync(string? categoria, string? busca, CancellationToken ct);
    Task<List<ProdutoDto>> ListarDestaquesAsync(CancellationToken ct);
    Task<ProdutoDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<ProdutoDto> CriarAsync(CriarProdutoRequest request, CancellationToken ct);
    Task<ProdutoDto> AtualizarAsync(Guid id, AtualizarProdutoRequest request, CancellationToken ct);
    Task RemoverAsync(Guid id, CancellationToken ct);
}
```

Create `src/RedeStore.Application/Produtos/ProdutoService.cs`:

```csharp
using RedeStore.Application.Common;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Produtos;

public sealed class ProdutoService : IProdutoService
{
    private readonly IProdutoRepository _produtoRepository;

    public ProdutoService(IProdutoRepository produtoRepository)
    {
        _produtoRepository = produtoRepository;
    }

    public async Task<List<ProdutoDto>> ListarAsync(string? categoria, string? busca, CancellationToken ct)
    {
        var produtos = await _produtoRepository.ListarAsync(ParseCategoria(categoria), busca, ct);
        return produtos.Select(MapearParaDto).ToList();
    }

    public async Task<List<ProdutoDto>> ListarDestaquesAsync(CancellationToken ct)
    {
        var produtos = await _produtoRepository.ListarDestaquesAsync(ct);
        return produtos.Select(MapearParaDto).ToList();
    }

    public async Task<ProdutoDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var produto = await _produtoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new ProdutoNaoEncontradoException($"Produto '{id}' não encontrado.");
        return MapearParaDto(produto);
    }

    public async Task<ProdutoDto> CriarAsync(CriarProdutoRequest request, CancellationToken ct)
    {
        var produto = new Produto
        {
            Id = Guid.NewGuid(),
            Nome = request.Nome,
            Categoria = ParseCategoriaObrigatoria(request.Categoria),
            Preco = request.Preco,
            Descricao = request.Descricao,
            Fotos = request.Fotos ?? [],
            Destaque = request.Destaque,
            Variacoes = request.Variacoes.Select(MapearVariacao).ToList(),
        };
        RecalcularTamanhosECores(produto);

        await _produtoRepository.AdicionarAsync(produto, ct);
        return MapearParaDto(produto);
    }

    public async Task<ProdutoDto> AtualizarAsync(Guid id, AtualizarProdutoRequest request, CancellationToken ct)
    {
        var produto = await _produtoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new ProdutoNaoEncontradoException($"Produto '{id}' não encontrado.");

        if (request.Nome is not null)
        {
            produto.Nome = request.Nome;
        }

        if (request.Categoria is not null)
        {
            produto.Categoria = ParseCategoriaObrigatoria(request.Categoria);
        }

        if (request.Preco is not null)
        {
            produto.Preco = request.Preco.Value;
        }

        if (request.Descricao is not null)
        {
            produto.Descricao = request.Descricao;
        }

        if (request.Fotos is not null)
        {
            produto.Fotos = request.Fotos;
        }

        if (request.Destaque is not null)
        {
            produto.Destaque = request.Destaque.Value;
        }

        if (request.Variacoes is not null)
        {
            produto.Variacoes.Clear();
            foreach (var variacao in request.Variacoes)
            {
                produto.Variacoes.Add(MapearVariacao(variacao));
            }
        }

        RecalcularTamanhosECores(produto);

        await _produtoRepository.AtualizarAsync(produto, ct);
        return MapearParaDto(produto);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct)
    {
        var produto = await _produtoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new ProdutoNaoEncontradoException($"Produto '{id}' não encontrado.");

        await _produtoRepository.RemoverAsync(produto, ct);
    }

    private static Variacao MapearVariacao(VariacaoRequest request) => new()
    {
        Id = Guid.NewGuid(),
        Tamanho = request.Tamanho,
        Cor = request.Cor,
        Estoque = request.Estoque,
    };

    private static void RecalcularTamanhosECores(Produto produto)
    {
        produto.Tamanhos = produto.Variacoes.Select(v => v.Tamanho).Distinct().ToList();
        produto.Cores = produto.Variacoes.Select(v => v.Cor).Distinct().ToList();
    }

    private static CategoriaProduto? ParseCategoria(string? valor) => valor switch
    {
        "camisetas" => CategoriaProduto.Camisetas,
        "moletons" => CategoriaProduto.Moletons,
        "acessorios" => CategoriaProduto.Acessorios,
        _ => null,
    };

    private static CategoriaProduto ParseCategoriaObrigatoria(string valor) =>
        ParseCategoria(valor) ?? throw new InvalidOperationException($"Categoria '{valor}' inválida.");

    private static ProdutoDto MapearParaDto(Produto produto) => new(
        produto.Id,
        produto.Nome,
        produto.Categoria.ToString().ToLowerInvariant(),
        produto.Preco,
        produto.Descricao,
        produto.Fotos,
        produto.Tamanhos,
        produto.Cores,
        produto.Destaque,
        produto.Variacoes.Select(v => new VariacaoDto(v.Id, v.Tamanho, v.Cor, v.Estoque)).ToList());
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.UnitTests --filter "FullyQualifiedName~ProdutoNaoEncontradoExceptionTests|FullyQualifiedName~ProdutoServiceTests"`
Expected: 11 passed.

- [ ] **Step 7: Register the service and validators in DI**

Open `src/RedeStore.Api/Program.cs`. Add these `using` statements:

```csharp
using RedeStore.Application.Produtos;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Application.Produtos.Validators;
```

Add these registrations right after the `IValidator<RedefinirSenhaRequest>` registration from Fase 2:

```csharp
builder.Services.AddScoped<IProdutoService, ProdutoService>();
builder.Services.AddSingleton<IValidator<CriarProdutoRequest>, CriarProdutoRequestValidator>();
builder.Services.AddSingleton<IValidator<AtualizarProdutoRequest>, AtualizarProdutoRequestValidator>();
```

- [ ] **Step 8: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 9: Commit**

```bash
git add src/RedeStore.Domain/Exceptions/ProdutoNaoEncontradoException.cs src/RedeStore.Application/Produtos/IProdutoService.cs src/RedeStore.Application/Produtos/ProdutoService.cs src/RedeStore.Api/Program.cs tests/RedeStore.UnitTests/Domain/ProdutoNaoEncontradoExceptionTests.cs tests/RedeStore.UnitTests/Produtos/
git commit -m "Add ProdutoService covering catalog CRUD and stock-derived tamanhos/cores"
```

---

## Task 4: Map the 6 Endpoints

**Files:**
- Create: `src/RedeStore.Api/Endpoints/ProdutosEndpoints.cs`
- Modify: `src/RedeStore.Api/Program.cs`
- Test: `tests/RedeStore.IntegrationTests/Produtos/ProdutosEndpointsSmokeTests.cs`

**Interfaces:**
- Consumes: `IProdutoService` (Task 3), `ValidationFilter<T>` (Fase 2), all DTOs from Task 2.
- Produces: the 6 HTTP routes from the requirements doc (section 3) — `GET /produtos`, `GET /produtos/destaques`, `GET /produtos/:id`, `POST /produtos`, `PATCH /produtos/:id`, `DELETE /produtos/:id`. Task 5's end-to-end tests exercise all of them together.

- [ ] **Step 1: Write the failing tests**

Create `tests/RedeStore.IntegrationTests/Produtos/ProdutosEndpointsSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Produtos;

[Collection(IntegrationTestCollection.Name)]
public class ProdutosEndpointsSmokeTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProdutosEndpointsSmokeTests(ApiFactory factory)
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

    private static CriarProdutoRequest RequestValido() => new(
        Nome: $"Camiseta {Guid.NewGuid()}",
        Categoria: "camisetas",
        Preco: 79.90m,
        Descricao: "Camiseta oficial",
        Fotos: ["https://exemplo.com/foto.jpg"],
        Destaque: false,
        Variacoes: [new VariacaoRequest("M", "Preto", 10)]);

    [Fact]
    public async Task PostProdutos_SemToken_Retorna401()
    {
        var response = await _client.PostAsJsonAsync("/produtos", RequestValido());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostProdutos_ComTokenDeAdmin_Retorna200EDepoisApareceNoDetalhe()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/produtos", RequestValido());
        Assert.Equal(HttpStatusCode.OK, criarResponse.StatusCode);
        var criado = await criarResponse.Content.ReadFromJsonAsync<ProdutoDto>();

        var detalheResponse = await _client.GetAsync($"/produtos/{criado!.Id}");
        Assert.Equal(HttpStatusCode.OK, detalheResponse.StatusCode);
    }

    [Fact]
    public async Task PostProdutos_SemVariacoes_Retorna400()
    {
        var token = await ObterTokenAdminAsync();
        using var clienteAdmin = _factory.CreateClient();
        clienteAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var request = RequestValido() with { Variacoes = [] };

        var response = await clienteAdmin.PostAsJsonAsync("/produtos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProdutoPorId_ComIdInexistente_Retorna404()
    {
        var response = await _client.GetAsync($"/produtos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~ProdutosEndpointsSmokeTests`
Expected: FAIL — routes return 404 (unmapped), since no endpoints are mapped yet.

- [ ] **Step 3: Implement the endpoint group**

Create `src/RedeStore.Api/Endpoints/ProdutosEndpoints.cs`:

```csharp
using RedeStore.Api.Filters;
using RedeStore.Application.Produtos;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class ProdutosEndpoints
{
    public static void MapProdutosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/produtos");

        grupo.MapGet("/", async (string? categoria, string? busca, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produtos = await produtoService.ListarAsync(categoria, busca, ct);
            return Results.Ok(produtos);
        });

        grupo.MapGet("/destaques", async (IProdutoService produtoService, CancellationToken ct) =>
        {
            var produtos = await produtoService.ListarDestaquesAsync(ct);
            return Results.Ok(produtos);
        });

        grupo.MapGet("/{id:guid}", async (Guid id, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.ObterPorIdAsync(id, ct);
            return Results.Ok(produto);
        });

        grupo.MapPost("/", async (CriarProdutoRequest request, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.CriarAsync(request, ct);
            return Results.Ok(produto);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<CriarProdutoRequest>>();

        grupo.MapPatch("/{id:guid}", async (Guid id, AtualizarProdutoRequest request, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.AtualizarAsync(id, request, ct);
            return Results.Ok(produto);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<AtualizarProdutoRequest>>();

        grupo.MapDelete("/{id:guid}", async (Guid id, IProdutoService produtoService, CancellationToken ct) =>
        {
            await produtoService.RemoverAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }
}
```

- [ ] **Step 4: Map the endpoint group in `Program.cs`**

Open `src/RedeStore.Api/Program.cs`. `RedeStore.Api.Endpoints` is already imported. Add this line right after `app.MapUsuariosEndpoints();`, before `app.Run();`:

```csharp
app.MapProdutosEndpoints();
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~ProdutosEndpointsSmokeTests`
Expected: 4 passed.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: everything passes, no regressions.

- [ ] **Step 7: Commit**

```bash
git add src/RedeStore.Api/Endpoints/ProdutosEndpoints.cs src/RedeStore.Api/Program.cs tests/RedeStore.IntegrationTests/Produtos/ProdutosEndpointsSmokeTests.cs
git commit -m "Map the 6 produtos endpoints"
```

---

## Task 5: End-to-End Tests Covering the Fase 3 Definition of Done

**Files:**
- Test: `tests/RedeStore.IntegrationTests/Produtos/ProdutoFlowEndToEndTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-4 — this task adds no production code, only tests that exercise the whole phase together against the real HTTP pipeline.

- [ ] **Step 1: Write the end-to-end test**

Create `tests/RedeStore.IntegrationTests/Produtos/ProdutoFlowEndToEndTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RedeStore.Application.Auth.Dtos;
using RedeStore.Application.Common;
using RedeStore.Application.Produtos.Dtos;
using RedeStore.Domain.Entities;
using Xunit;

namespace RedeStore.IntegrationTests.Produtos;

[Collection(IntegrationTestCollection.Name)]
public class ProdutoFlowEndToEndTests
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ProdutoFlowEndToEndTests(ApiFactory factory)
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

    [Fact]
    public async Task FluxoCompleto_CriarListarDetalharAtualizarDeletar_FuncionaPontaAPonta()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var nomeUnico = $"Camiseta {Guid.NewGuid()}";

        var criarResponse = await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: nomeUnico,
            Categoria: "camisetas",
            Preco: 79.90m,
            Descricao: "Camiseta oficial da Rede",
            Fotos: ["https://exemplo.com/foto.jpg"],
            Destaque: true,
            Variacoes: [new VariacaoRequest("M", "Preto", 10), new VariacaoRequest("G", "Branco", 5)]));
        criarResponse.EnsureSuccessStatusCode();
        var criado = await criarResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(["M", "G"], criado!.Tamanhos);
        Assert.Equal(["Preto", "Branco"], criado.Cores);

        var listaResponse = await _client.GetAsync($"/produtos?busca={Uri.EscapeDataString(nomeUnico)}");
        listaResponse.EnsureSuccessStatusCode();
        var lista = await listaResponse.Content.ReadFromJsonAsync<List<ProdutoDto>>();
        Assert.Contains(lista!, p => p.Id == criado.Id);

        var destaquesResponse = await _client.GetAsync("/produtos/destaques");
        destaquesResponse.EnsureSuccessStatusCode();
        var destaques = await destaquesResponse.Content.ReadFromJsonAsync<List<ProdutoDto>>();
        Assert.Contains(destaques!, p => p.Id == criado.Id);

        var detalheResponse = await _client.GetAsync($"/produtos/{criado.Id}");
        detalheResponse.EnsureSuccessStatusCode();
        var detalhe = await detalheResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(2, detalhe!.Variacoes.Count);

        var atualizarResponse = await clienteAdmin.PatchAsJsonAsync($"/produtos/{criado.Id}",
            new AtualizarProdutoRequest(null, null, null, null, null, null, [new VariacaoRequest("U", "Azul", 3)]));
        atualizarResponse.EnsureSuccessStatusCode();
        var atualizado = await atualizarResponse.Content.ReadFromJsonAsync<ProdutoDto>();
        Assert.Equal(["U"], atualizado!.Tamanhos);
        Assert.Equal(["Azul"], atualizado.Cores);
        Assert.Single(atualizado.Variacoes);

        var deletarResponse = await clienteAdmin.DeleteAsync($"/produtos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deletarResponse.StatusCode);

        var depoisDeDeletarResponse = await _client.GetAsync($"/produtos/{criado.Id}");
        Assert.Equal(HttpStatusCode.NotFound, depoisDeDeletarResponse.StatusCode);
    }

    [Fact]
    public async Task PatchProdutos_SemTokenDeAdmin_Retorna401()
    {
        var response = await _client.PatchAsJsonAsync($"/produtos/{Guid.NewGuid()}",
            new AtualizarProdutoRequest("Novo Nome", null, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProdutos_SemTokenDeAdmin_Retorna401()
    {
        var response = await _client.DeleteAsync($"/produtos/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProdutos_ComFiltroDeCategoria_RetornaSoDaCategoria()
    {
        using var clienteAdmin = await CriarClienteAdminAsync();
        var nomeMoletom = $"Moletom {Guid.NewGuid()}";
        await clienteAdmin.PostAsJsonAsync("/produtos", new CriarProdutoRequest(
            Nome: nomeMoletom,
            Categoria: "moletons",
            Preco: 149.90m,
            Descricao: "Moletom oficial",
            Fotos: [],
            Destaque: false,
            Variacoes: [new VariacaoRequest("G", "Cinza", 5)]));

        var response = await _client.GetAsync("/produtos?categoria=moletons");
        response.EnsureSuccessStatusCode();
        var lista = await response.Content.ReadFromJsonAsync<List<ProdutoDto>>();

        Assert.Contains(lista!, p => p.Nome == nomeMoletom);
        Assert.All(lista!, p => Assert.Equal("moletons", p.Categoria));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (if anything from Tasks 1-4 was missed, this is where it surfaces)**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~ProdutoFlowEndToEndTests`
Expected: if Tasks 1-4 were done correctly, these should mostly pass already — this task is verification, not new implementation. If something fails, fix the gap in the relevant earlier layer (repository, service, or endpoint) rather than adding a workaround here.

- [ ] **Step 3: Run the tests to verify they pass**

Run: `dotnet test tests/RedeStore.IntegrationTests --filter FullyQualifiedName~ProdutoFlowEndToEndTests`
Expected: 4 passed.

- [ ] **Step 4: Run the entire solution's test suite one final time**

Run: `dotnet test`
Expected: all tests across `RedeStore.UnitTests` and `RedeStore.IntegrationTests` pass.

- [ ] **Step 5: Commit**

```bash
git add tests/RedeStore.IntegrationTests/Produtos/ProdutoFlowEndToEndTests.cs
git commit -m "Add end-to-end tests covering the Fase 3 Definition of Done"
```

---

## Definition of Done (Fase 3)

- `dotnet test` passes for the entire solution, including a true end-to-end run of criar produto (admin) → aparece em `/produtos` (com filtro de categoria e de busca) e em `/produtos/destaques` → `GET /produtos/:id` retorna variações e `tamanhos`/`cores` recalculados → `PATCH` atualiza campos parciais e recalcula `tamanhos`/`cores` ao trocar `variacoes` → `DELETE` remove o produto e um novo `GET` retorna 404.
- Um produto não pode ser criado sem nenhuma variação, nem com `preco <= 0`, nem com uma `categoria` fora do enum de 3 valores.
- `POST`/`PATCH`/`DELETE /produtos` exigem papel `admin` (401 sem token); `GET /produtos`, `GET /produtos/destaques` e `GET /produtos/:id` são públicos.
- `Tamanhos`/`Cores` nunca são aceitos verbatim do client — são sempre recalculados a partir de `Variacoes` no momento do save.
- Decremento de estoque na compra continua fora de escopo — isso é Fase 5, junto de Pedidos.
