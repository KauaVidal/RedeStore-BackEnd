namespace RedeStore.Domain.Entities;

public class Produto
{
    public Guid Id { get; set; }
    public required string Nome { get; set; }
    public CategoriaProduto Categoria { get; set; }
    public decimal Preco { get; set; }
    public required string Descricao { get; set; }
    public List<string> Fotos { get; set; } = [];
    public List<string> Tamanhos { get; set; } = [];
    public List<string> Cores { get; set; } = [];
    public bool Destaque { get; set; }
    public List<Variacao> Variacoes { get; set; } = [];
}
