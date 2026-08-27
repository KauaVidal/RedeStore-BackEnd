namespace RedeStore.Domain.Entities;

public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UsuarioId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiraEm { get; set; }
    public DateTime? UsadoEm { get; set; }
}
