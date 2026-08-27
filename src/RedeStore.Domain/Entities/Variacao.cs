namespace RedeStore.Domain.Entities;

public class Variacao
{
    public Guid Id { get; set; }
    public Guid ProdutoId { get; set; }
    public required string Tamanho { get; set; }
    public required string Cor { get; set; }
    public int Estoque { get; set; }
}
