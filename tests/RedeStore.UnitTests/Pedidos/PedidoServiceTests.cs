using RedeStore.Application.Common;
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
    public async Task CriarAsync_ComMultiplosItens_DecrementaEstoqueEmOrdemDeterministicaPorVariacaoIdENaoPelaOrdemDoRequest()
    {
        var produtoAId = await CriarProdutoComVariacaoAsync(estoque: 10);
        var produtoBId = await CriarProdutoComVariacaoAsync(estoque: 10);
        var variacaoAId = (await _produtoRepositorio.BuscarPorIdAsync(produtoAId, CancellationToken.None))!.Variacoes[0].Id;
        var variacaoBId = (await _produtoRepositorio.BuscarPorIdAsync(produtoBId, CancellationToken.None))!.Variacoes[0].Id;
        var ordemEsperadaPorId = new[] { variacaoAId, variacaoBId }.OrderBy(id => id).ToList();

        // Envia no request o item de MAIOR Id de variação primeiro, propositalmente na
        // ordem oposta à ordem de decremento esperada (por Id), para provar que o
        // decremento não segue a ordem em que os itens chegaram no request.
        var produtoIdDoMaiorId = variacaoAId == ordemEsperadaPorId[1] ? produtoAId : produtoBId;
        var produtoIdDoMenorId = variacaoAId == ordemEsperadaPorId[0] ? produtoAId : produtoBId;
        var request = new CriarPedidoRequest(
            Itens: [
                new ItemPedidoRequest(produtoIdDoMaiorId, "M", "Preto", 1),
                new ItemPedidoRequest(produtoIdDoMenorId, "M", "Preto", 1),
            ],
            FormaEntrega: "retirada",
            Endereco: null);

        await _sut.CriarAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(ordemEsperadaPorId, _variacaoRepositorio.OrdemDeChamadas);
    }

    [Fact]
    public async Task CriarAsync_ComMultiplosItens_QuandoItemProcessadoPrimeiroNaOrdemDeLockFalhaPorEstoque_NaoTocaNoItemAindaNaoTentado()
    {
        var produtoAId = await CriarProdutoComVariacaoAsync(estoque: 10);
        var produtoBId = await CriarProdutoComVariacaoAsync(estoque: 10);
        var produtoA = (await _produtoRepositorio.BuscarPorIdAsync(produtoAId, CancellationToken.None))!;
        var produtoB = (await _produtoRepositorio.BuscarPorIdAsync(produtoBId, CancellationToken.None))!;

        // Identifica, em tempo de execução, qual dos dois produtos tem a variação de
        // MENOR Id - esse é o que a passada de decremento (ordenada por Variacao.Id)
        // tenta primeiro.
        var (produtoIdMenorId, produtoIdMaiorId) = produtoA.Variacoes[0].Id.CompareTo(produtoB.Variacoes[0].Id) < 0
            ? (produtoAId, produtoBId)
            : (produtoBId, produtoAId);

        // Pede uma quantidade maior que o estoque disponível para o item de menor Id (o
        // primeiro a ser tentado), garantindo que a exceção seja lançada ANTES que o item
        // de maior Id - enviado como item 1 no request - seja sequer tentado.
        var request = new CriarPedidoRequest(
            Itens: [
                new ItemPedidoRequest(produtoIdMaiorId, "M", "Preto", 1),
                new ItemPedidoRequest(produtoIdMenorId, "M", "Preto", 999),
            ],
            FormaEntrega: "retirada",
            Endereco: null);

        await Assert.ThrowsAsync<EstoqueInsuficienteException>(() =>
            _sut.CriarAsync(Guid.NewGuid(), request, CancellationToken.None));

        var produtoMaiorIdApos = await _produtoRepositorio.BuscarPorIdAsync(produtoIdMaiorId, CancellationToken.None);
        Assert.Equal(10, produtoMaiorIdApos!.Variacoes[0].Estoque);

        // Confirma que o item de maior Id nunca foi sequer tentado: apenas uma chamada de
        // decremento ocorreu (a do item de menor Id, que falhou).
        Assert.Single(_variacaoRepositorio.OrdemDeChamadas);

        var pedidos = await _sut.ListarTodosAsync(CancellationToken.None);
        Assert.Empty(pedidos);
    }

    [Fact]
    public async Task CriarAsync_ComMesmoProdutoEmDuasLinhas_BuscaOProdutoApenasUmaVezEDecrementaAQuantidadeTotal()
    {
        var produtoId = await CriarProdutoComVariacaoAsync(estoque: 10, preco: 25m);
        var produtoRepositorioContando = new ContandoProdutoRepository(_produtoRepositorio);
        var variacaoRepositorio = new FakeVariacaoRepository(produtoRepositorioContando);
        var sut = new PedidoService(produtoRepositorioContando, variacaoRepositorio, _pedidoRepositorio, new FakeUnitOfWork());
        var request = new CriarPedidoRequest(
            Itens: [
                new ItemPedidoRequest(produtoId, "M", "Preto", 2),
                new ItemPedidoRequest(produtoId, "M", "Preto", 3),
            ],
            FormaEntrega: "retirada",
            Endereco: null);

        var pedido = await sut.CriarAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(1, produtoRepositorioContando.ChamadasBuscarPorId);
        Assert.Equal(2, pedido.Itens.Count);
        Assert.All(pedido.Itens, i => Assert.Equal("Camiseta Rede", i.Nome));
        Assert.All(pedido.Itens, i => Assert.Equal(25m, i.PrecoUnitario));
        Assert.Equal(125m, pedido.ValorTotal);

        var produtoAtualizado = await _produtoRepositorio.BuscarPorIdAsync(produtoId, CancellationToken.None);
        Assert.Equal(5, produtoAtualizado!.Variacoes[0].Estoque);
    }

    private sealed class ContandoProdutoRepository : IProdutoRepository
    {
        private readonly IProdutoRepository _interno;

        public ContandoProdutoRepository(IProdutoRepository interno) => _interno = interno;

        public int ChamadasBuscarPorId { get; private set; }

        public Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct) =>
            _interno.ListarAsync(categoria, busca, ct);

        public Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct) => _interno.ListarDestaquesAsync(ct);

        public Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct)
        {
            ChamadasBuscarPorId++;
            return _interno.BuscarPorIdAsync(id, ct);
        }

        public Task AdicionarAsync(Produto produto, CancellationToken ct) => _interno.AdicionarAsync(produto, ct);

        public Task AtualizarAsync(Produto produto, CancellationToken ct) => _interno.AtualizarAsync(produto, ct);

        public Task RemoverAsync(Produto produto, CancellationToken ct) => _interno.RemoverAsync(produto, ct);
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
