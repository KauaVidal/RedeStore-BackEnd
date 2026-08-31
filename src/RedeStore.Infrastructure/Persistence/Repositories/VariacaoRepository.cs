using System.Linq;
using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class VariacaoRepository : IVariacaoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public VariacaoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct)
    {
        var linhasAfetadas = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Variacoes\" SET \"Estoque\" = \"Estoque\" - {quantidade} WHERE \"Id\" = {variacaoId} AND \"Estoque\" >= {quantidade}", ct);

        if (linhasAfetadas > 0)
        {
            // ExecuteSqlInterpolatedAsync bypasses the change tracker, so a Variacao already
            // tracked in this DbContext instance (e.g. just loaded via ProdutoRepository) would
            // otherwise keep serving its stale in-memory Estoque value. Reload it from the database.
            var entrada = _dbContext.ChangeTracker.Entries<Variacao>()
                .FirstOrDefault(e => e.Entity.Id == variacaoId);
            if (entrada is not null)
            {
                await entrada.ReloadAsync(ct);
            }
        }

        return linhasAfetadas > 0;
    }
}
