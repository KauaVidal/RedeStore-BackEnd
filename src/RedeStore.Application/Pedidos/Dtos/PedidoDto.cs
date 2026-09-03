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
