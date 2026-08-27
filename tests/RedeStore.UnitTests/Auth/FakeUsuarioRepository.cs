using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Auth;

public sealed class FakeUsuarioRepository : IUsuarioRepository
{
    private readonly Dictionary<Guid, Usuario> _usuariosPorId = new();

    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(_usuariosPorId.Values.SingleOrDefault(u => u.Email == email));

    public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_usuariosPorId.GetValueOrDefault(id));

    public Task AdicionarAsync(Usuario usuario, CancellationToken ct)
    {
        _usuariosPorId[usuario.Id] = usuario;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Usuario usuario, CancellationToken ct)
    {
        _usuariosPorId[usuario.Id] = usuario;
        return Task.CompletedTask;
    }
}
