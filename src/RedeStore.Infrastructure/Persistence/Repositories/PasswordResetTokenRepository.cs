using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository : IPasswordResetTokenRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public PasswordResetTokenRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AdicionarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _dbContext.PasswordResetTokens.Add(token);
        await _dbContext.SaveChangesAsync(ct);
    }

    public Task<PasswordResetToken?> BuscarPorTokenHashAsync(string tokenHash, CancellationToken ct) =>
        _dbContext.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AtualizarAsync(PasswordResetToken token, CancellationToken ct)
    {
        _dbContext.PasswordResetTokens.Update(token);
        await _dbContext.SaveChangesAsync(ct);
    }
}
