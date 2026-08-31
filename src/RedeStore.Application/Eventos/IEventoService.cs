using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Application.Eventos;

public interface IEventoService
{
    Task<List<EventoDto>> ListarAsync(bool apenasFuturos, CancellationToken ct);
    Task<EventoDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<VagasRestantesDto> ObterVagasRestantesAsync(Guid id, CancellationToken ct);
    Task<EventoDto> CriarAsync(CriarEventoRequest request, CancellationToken ct);
    Task<EventoDto> AtualizarAsync(Guid id, AtualizarEventoRequest request, CancellationToken ct);
    Task RemoverAsync(Guid id, CancellationToken ct);
}
