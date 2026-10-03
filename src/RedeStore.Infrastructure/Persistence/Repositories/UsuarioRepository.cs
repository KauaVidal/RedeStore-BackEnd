using Microsoft.EntityFrameworkCore;
using Npgsql;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class UsuarioRepository : IUsuarioRepository
{
    private const string IndiceEmailUnico = "IX_Usuarios_Email";

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
        await SalvarAsync(usuario, ct);
    }

    public async Task AtualizarAsync(Usuario usuario, CancellationToken ct)
    {
        _dbContext.Usuarios.Update(usuario);
        await SalvarAsync(usuario, ct);
    }

    // A checagem do AuthService não cobre dois cadastros simultâneos com o mesmo e-mail:
    // o índice único barra o segundo, e aqui isso vira 409 em vez de 500.
    private async Task SalvarAsync(Usuario usuario, CancellationToken ct)
    {
        try
        {
            await _dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: IndiceEmailUnico,
        })
        {
            _dbContext.Entry(usuario).State = EntityState.Detached;
            throw new EmailEmUsoException($"O e-mail '{usuario.Email}' já está em uso.");
        }
    }
}
