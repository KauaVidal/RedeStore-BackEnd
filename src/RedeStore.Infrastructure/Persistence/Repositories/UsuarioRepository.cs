using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class UsuarioRepository : IUsuarioRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public UsuarioRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct) =>
        _dbContext.Usuarios.SingleOrDefaultAsync(u => u.Email == email, ct);

    public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);

    public async Task AdicionarAsync(Usuario usuario, CancellationToken ct)
    {
        _dbContext.Usuarios.Add(usuario);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Usuario usuario, CancellationToken ct)
    {
        _dbContext.Usuarios.Update(usuario);
        await _dbContext.SaveChangesAsync(ct);
    }
}
