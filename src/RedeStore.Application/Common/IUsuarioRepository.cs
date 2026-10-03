using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IUsuarioRepository
{
    /// <summary>Busca pelo e-mail já normalizado (ver <c>EmailUsuario.Normalizar</c>).</summary>
    Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct);

    /// <summary>Lança <c>EmailEmUsoException</c> se outro usuário já tiver o e-mail (inclusive em cadastros simultâneos).</summary>
    Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Usuario usuario, CancellationToken ct);
    /// <summary>Lança <c>EmailEmUsoException</c> se outro usuário já tiver o e-mail.</summary>
    Task AtualizarAsync(Usuario usuario, CancellationToken ct);
}
