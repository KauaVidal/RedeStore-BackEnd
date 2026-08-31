using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class InscricaoRepository : IInscricaoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public InscricaoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct) =>
        _dbContext.Inscricoes.SingleOrDefaultAsync(
            i => i.EventoId == eventoId && i.UsuarioId == usuarioId && i.Status == StatusInscricao.Confirmada, ct);

    public Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Inscricoes.SingleOrDefaultAsync(i => i.Id == id, ct);

    public Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        _dbContext.Inscricoes.Where(i => i.UsuarioId == usuarioId).OrderByDescending(i => i.CriadoEm).ToListAsync(ct);

    public Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct) =>
        _dbContext.Inscricoes.Where(i => i.EventoId == eventoId).OrderByDescending(i => i.CriadoEm).ToListAsync(ct);

    public async Task AdicionarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _dbContext.Inscricoes.Add(inscricao);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _dbContext.Inscricoes.Update(inscricao);
        await _dbContext.SaveChangesAsync(ct);
    }
}
