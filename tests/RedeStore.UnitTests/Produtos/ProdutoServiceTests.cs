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
