namespace RedeStore.Domain.Entities;

public class Pedido
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public List<ItemPedido> Itens { get; set; } = [];
    public FormaEntrega FormaEntrega { get; set; }
    public Endereco? Endereco { get; set; }
    public decimal ValorTotal { get; set; }
    public StatusPedido Status { get; set; }
    public DateTime CriadoEm { get; set; }
}
