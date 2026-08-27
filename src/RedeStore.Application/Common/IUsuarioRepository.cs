using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IUsuarioRepository
{
    Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct);
    Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Usuario usuario, CancellationToken ct);
    Task AtualizarAsync(Usuario usuario, CancellationToken ct);
}
