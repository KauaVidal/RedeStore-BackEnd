namespace RedeStore.Domain.Entities;

public class Evento
{
    public Guid Id { get; set; }
    public required string Titulo { get; set; }
    public required string Descricao { get; set; }
    public DateTime DataHora { get; set; }
    public required string Local { get; set; }
    public decimal Preco { get; set; }
    public int VagasTotais { get; set; }
    public required string Foto { get; set; }
    public List<Inscricao> Inscricoes { get; set; } = [];
}
