using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IPasswordResetTokenRepository
{
    Task AdicionarAsync(PasswordResetToken token, CancellationToken ct);
    Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct);
    Task AtualizarAsync(PasswordResetToken token, CancellationToken ct);
}
