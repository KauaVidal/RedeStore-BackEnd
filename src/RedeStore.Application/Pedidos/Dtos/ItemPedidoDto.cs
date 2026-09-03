namespace RedeStore.Application.Pedidos.Dtos;

public sealed record ItemPedidoDto(
    Guid ProdutoId,
    string Nome,
    decimal PrecoUnitario,
    string FotoUrl,
    string Tamanho,
    string Cor,
    int Quantidade);
