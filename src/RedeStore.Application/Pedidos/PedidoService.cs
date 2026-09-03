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
