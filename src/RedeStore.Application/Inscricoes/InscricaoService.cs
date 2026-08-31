using RedeStore.Application.Common;
using RedeStore.Application.Inscricoes.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Inscricoes;

public sealed class InscricaoService : IInscricaoService
{
    private readonly IEventoRepository _eventoRepository;
    private readonly IInscricaoRepository _inscricaoRepository;
    private readonly IUnitOfWork _unitOfWork;

    public InscricaoService(IEventoRepository eventoRepository, IInscricaoRepository inscricaoRepository, IUnitOfWork unitOfWork)
    {
        _eventoRepository = eventoRepository;
        _inscricaoRepository = inscricaoRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ResultadoInscricaoDto> InscreverAsync(Guid eventoId, Guid usuarioId, CancellationToken ct)
    {
        await using var transacao = await _unitOfWork.IniciarTransacaoAsync(ct);

        var resultadoLock = await _eventoRepository.LockAndCountInscricoesConfirmadasAsync(eventoId, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{eventoId}' não encontrado.");

        var existente = await _inscricaoRepository.BuscarConfirmadaPorEventoEUsuarioAsync(eventoId, usuarioId, ct);
        if (existente is not null)
        {
            await transacao.ConfirmarAsync(ct);
            return new ResultadoInscricaoDto("ja_inscrito", MapearParaDto(existente));
        }

        if (resultadoLock.VagasConfirmadas >= resultadoLock.Evento.VagasTotais)
        {
            await transacao.ConfirmarAsync(ct);
            return new ResultadoInscricaoDto("esgotado", null);
        }

        var inscricao = new Inscricao
        {
            Id = Guid.NewGuid(),
            EventoId = eventoId,
            UsuarioId = usuarioId,
            Status = StatusInscricao.Confirmada,
            ValorPago = resultadoLock.Evento.Preco,
            CriadoEm = DateTime.UtcNow,
        };
        await _inscricaoRepository.AdicionarAsync(inscricao, ct);
        await transacao.ConfirmarAsync(ct);

        return new ResultadoInscricaoDto("criada", MapearParaDto(inscricao));
    }

    public async Task<List<InscricaoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct)
    {
        var inscricoes = await _inscricaoRepository.ListarPorUsuarioAsync(usuarioId, ct);
        return inscricoes.Select(MapearParaDto).ToList();
    }

    public async Task<List<InscricaoDto>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct)
    {
        var inscricoes = await _inscricaoRepository.ListarPorEventoAsync(eventoId, ct);
        return inscricoes.Select(MapearParaDto).ToList();
    }

    public async Task<InscricaoDto> CancelarAsync(Guid inscricaoId, Guid idUsuarioLogado, bool ehAdmin, CancellationToken ct)
    {
        var inscricao = await _inscricaoRepository.BuscarPorIdAsync(inscricaoId, ct)
            ?? throw new InscricaoNaoEncontradaException($"Inscrição '{inscricaoId}' não encontrada.");

        if (inscricao.UsuarioId != idUsuarioLogado && !ehAdmin)
        {
            throw new AcessoNegadoException("Você não tem permissão para cancelar esta inscrição.");
        }

        inscricao.Status = StatusInscricao.Cancelada;
        await _inscricaoRepository.AtualizarAsync(inscricao, ct);
        return MapearParaDto(inscricao);
    }

    private static InscricaoDto MapearParaDto(Inscricao inscricao) => new(
        inscricao.Id,
        inscricao.EventoId,
        inscricao.UsuarioId,
        inscricao.Status.ToString().ToLowerInvariant(),
        inscricao.ValorPago,
        inscricao.CriadoEm);
}
