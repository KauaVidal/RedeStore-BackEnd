namespace RedeStore.Domain.Entities;

public class Inscricao
{
    public Guid Id { get; set; }
    public Guid EventoId { get; set; }
    public Guid UsuarioId { get; set; }
    public StatusInscricao Status { get; set; }
    public decimal ValorPago { get; set; }
    public DateTime CriadoEm { get; set; }
}
