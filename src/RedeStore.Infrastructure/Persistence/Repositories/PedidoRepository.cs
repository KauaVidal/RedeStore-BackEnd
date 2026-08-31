using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class PedidoRepository : IPedidoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public PedidoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<List<Pedido>> ListarTodosAsync(CancellationToken ct) =>
        _dbContext.Pedidos.Include(p => p.Itens).OrderByDescending(p => p.CriadoEm).ToListAsync(ct);

    public Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        _dbContext.Pedidos.Include(p => p.Itens).Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.CriadoEm).ToListAsync(ct);

    public Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Pedidos.Include(p => p.Itens).SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AdicionarAsync(Pedido pedido, CancellationToken ct)
    {
        _dbContext.Pedidos.Add(pedido);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Pedido pedido, CancellationToken ct)
    {
        await _dbContext.SaveChangesAsync(ct);
    }
}
