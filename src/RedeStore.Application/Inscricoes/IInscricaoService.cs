using RedeStore.Application.Inscricoes.Dtos;

namespace RedeStore.Application.Inscricoes;

public interface IInscricaoService
{
    Task<ResultadoInscricaoDto> InscreverAsync(Guid eventoId, Guid usuarioId, CancellationToken ct);
    Task<List<InscricaoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<List<InscricaoDto>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct);
    Task<InscricaoDto> CancelarAsync(Guid inscricaoId, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct);
}
