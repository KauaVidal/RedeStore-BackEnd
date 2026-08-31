using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IInscricaoRepository
{
    Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct);
    Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct);
    Task AdicionarAsync(Inscricao inscricao, CancellationToken ct);
    Task AtualizarAsync(Inscricao inscricao, CancellationToken ct);
}
