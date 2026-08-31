namespace RedeStore.Domain.Entities;

public class ItemPedido
{
    public Guid Id { get; set; }
    public Guid PedidoId { get; set; }
    public Guid ProdutoId { get; set; }
    public required string Nome { get; set; }
    public decimal PrecoUnitario { get; set; }
    public required string FotoUrl { get; set; }
    public required string Tamanho { get; set; }
    public required string Cor { get; set; }
    public int Quantidade { get; set; }
}
