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
