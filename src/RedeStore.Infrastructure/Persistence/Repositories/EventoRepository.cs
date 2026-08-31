using Microsoft.EntityFrameworkCore;
using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.Infrastructure.Persistence.Repositories;

public sealed class EventoRepository : IEventoRepository
{
    private readonly RedeStoreDbContext _dbContext;

    public EventoRepository(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct)
    {
        var query = _dbContext.Eventos.Include(e => e.Inscricoes).AsQueryable();

        if (apenasFuturos)
        {
            var agora = DateTime.UtcNow;
            query = query.Where(e => e.DataHora >= agora);
        }

        return await query.ToListAsync(ct);
    }

    public Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        _dbContext.Eventos.Include(e => e.Inscricoes).SingleOrDefaultAsync(e => e.Id == id, ct);

    public async Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct)
    {
        var linhaBloqueada = await _dbContext.Database
            .SqlQueryRaw<int>("SELECT 1 FROM \"Eventos\" WHERE \"Id\" = {0} FOR UPDATE", eventoId)
            .ToListAsync(ct);

        if (linhaBloqueada.Count == 0)
        {
            return null;
        }

        var evento = await _dbContext.Eventos.AsNoTracking().SingleAsync(e => e.Id == eventoId, ct);
        var vagasConfirmadas = await _dbContext.Inscricoes
            .CountAsync(i => i.EventoId == eventoId && i.Status == StatusInscricao.Confirmada, ct);

        return (evento, vagasConfirmadas);
    }

    public async Task AdicionarAsync(Evento evento, CancellationToken ct)
    {
        _dbContext.Eventos.Add(evento);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AtualizarAsync(Evento evento, CancellationToken ct)
    {
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task RemoverAsync(Evento evento, CancellationToken ct)
    {
        _dbContext.Eventos.Remove(evento);
        await _dbContext.SaveChangesAsync(ct);
    }
}
