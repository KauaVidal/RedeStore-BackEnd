using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Eventos;

public sealed class FakeEventoRepository : IEventoRepository
{
    public Dictionary<Guid, Evento> EventosPorId { get; } = new();

    public Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct)
    {
        var query = EventosPorId.Values.AsEnumerable();

        if (apenasFuturos)
        {
            var agora = DateTime.UtcNow;
            query = query.Where(e => e.DataHora >= agora);
        }

        return Task.FromResult(query.ToList());
    }

    public Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(EventosPorId.GetValueOrDefault(id));

    public Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct)
    {
        if (!EventosPorId.TryGetValue(eventoId, out var evento))
        {
            return Task.FromResult<(Evento, int)?>(null);
        }

        var vagasConfirmadas = evento.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada);
        return Task.FromResult<(Evento, int)?>((evento, vagasConfirmadas));
    }

    public Task AdicionarAsync(Evento evento, CancellationToken ct)
    {
        EventosPorId[evento.Id] = evento;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Evento evento, CancellationToken ct)
    {
        EventosPorId[evento.Id] = evento;
        return Task.CompletedTask;
    }

    public Task RemoverAsync(Evento evento, CancellationToken ct)
    {
        EventosPorId.Remove(evento.Id);
        return Task.CompletedTask;
    }
}
