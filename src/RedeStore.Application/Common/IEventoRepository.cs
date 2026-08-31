using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IEventoRepository
{
    Task<List<Evento>> ListarAsync(bool apenasFuturos, CancellationToken ct);
    Task<Evento?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task<(Evento Evento, int VagasConfirmadas)?> LockAndCountInscricoesConfirmadasAsync(Guid eventoId, CancellationToken ct);
    Task AdicionarAsync(Evento evento, CancellationToken ct);
    Task AtualizarAsync(Evento evento, CancellationToken ct);
    Task RemoverAsync(Evento evento, CancellationToken ct);
}
