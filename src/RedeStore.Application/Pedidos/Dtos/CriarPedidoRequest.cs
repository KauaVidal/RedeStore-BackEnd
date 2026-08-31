namespace RedeStore.Application.Pedidos.Dtos;

public sealed record CriarPedidoRequest(List<ItemPedidoRequest> Itens, string FormaEntrega, EnderecoDto? Endereco);
