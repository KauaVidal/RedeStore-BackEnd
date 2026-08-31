using RedeStore.Application.Common;
using RedeStore.Application.Eventos.Dtos;
using RedeStore.Domain.Entities;
using RedeStore.Domain.Exceptions;

namespace RedeStore.Application.Eventos;

public sealed class EventoService : IEventoService
{
    private readonly IEventoRepository _eventoRepository;
    private readonly IUnitOfWork _unitOfWork;

    public EventoService(IEventoRepository eventoRepository, IUnitOfWork unitOfWork)
    {
        _eventoRepository = eventoRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<List<EventoDto>> ListarAsync(bool apenasFuturos, CancellationToken ct)
    {
        var eventos = await _eventoRepository.ListarAsync(apenasFuturos, ct);
        return eventos.Select(MapearParaDto).ToList();
    }

    public async Task<EventoDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");
        return MapearParaDto(evento);
    }

    public async Task<VagasRestantesDto> ObterVagasRestantesAsync(Guid id, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");
        return new VagasRestantesDto(CalcularVagasRestantes(evento));
    }

    public async Task<EventoDto> CriarAsync(CriarEventoRequest request, CancellationToken ct)
    {
        var evento = new Evento
        {
            Id = Guid.NewGuid(),
            Titulo = request.Titulo,
            Descricao = request.Descricao,
            DataHora = request.DataHora,
            Local = request.Local,
            Preco = request.Preco,
            VagasTotais = request.VagasTotais,
            Foto = request.Foto,
        };

        await _eventoRepository.AdicionarAsync(evento, ct);
        return MapearParaDto(evento);
    }

    public async Task<EventoDto> AtualizarAsync(Guid id, AtualizarEventoRequest request, CancellationToken ct)
    {
        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");

        if (request.VagasTotais is not null)
        {
            var vagasConfirmadas = evento.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada);
            if (request.VagasTotais.Value < vagasConfirmadas)
            {
                throw new EventoVagasTotaisInsuficientesException(
                    $"Não é possível reduzir vagasTotais para {request.VagasTotais.Value}: há {vagasConfirmadas} inscrições confirmadas.");
            }
        }

        if (request.Titulo is not null)
        {
            evento.Titulo = request.Titulo;
        }

        if (request.Descricao is not null)
        {
            evento.Descricao = request.Descricao;
        }

        if (request.DataHora is not null)
        {
            evento.DataHora = request.DataHora.Value;
        }

        if (request.Local is not null)
        {
            evento.Local = request.Local;
        }

        if (request.Preco is not null)
        {
            evento.Preco = request.Preco.Value;
        }

        if (request.VagasTotais is not null)
        {
            evento.VagasTotais = request.VagasTotais.Value;
        }

        if (request.Foto is not null)
        {
            evento.Foto = request.Foto;
        }

        await _eventoRepository.AtualizarAsync(evento, ct);
        return MapearParaDto(evento);
    }

    public async Task RemoverAsync(Guid id, CancellationToken ct)
    {
        await using var transacao = await _unitOfWork.IniciarTransacaoAsync(ct);

        var resultadoLock = await _eventoRepository.LockAndCountInscricoesConfirmadasAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");

        if (resultadoLock.VagasConfirmadas > 0)
        {
            throw new EventoComInscricoesConfirmadasException(
                $"Evento '{id}' tem inscrições confirmadas e não pode ser removido.");
        }

        var evento = await _eventoRepository.BuscarPorIdAsync(id, ct)
            ?? throw new EventoNaoEncontradoException($"Evento '{id}' não encontrado.");

        await _eventoRepository.RemoverAsync(evento, ct);
        await transacao.ConfirmarAsync(ct);
    }

    private static int CalcularVagasRestantes(Evento evento) =>
        evento.VagasTotais - evento.Inscricoes.Count(i => i.Status == StatusInscricao.Confirmada);

    private static EventoDto MapearParaDto(Evento evento) => new(
        evento.Id,
        evento.Titulo,
        evento.Descricao,
        evento.DataHora,
        evento.Local,
        evento.Preco,
        evento.VagasTotais,
        CalcularVagasRestantes(evento),
        evento.Foto);
}
