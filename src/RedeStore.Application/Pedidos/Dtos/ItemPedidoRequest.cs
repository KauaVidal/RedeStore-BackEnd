namespace RedeStore.Application.Pedidos.Dtos;

public sealed record ItemPedidoRequest(Guid ProdutoId, string Tamanho, string Cor, int Quantidade);
