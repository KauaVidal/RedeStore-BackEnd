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
