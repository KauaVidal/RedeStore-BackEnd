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
