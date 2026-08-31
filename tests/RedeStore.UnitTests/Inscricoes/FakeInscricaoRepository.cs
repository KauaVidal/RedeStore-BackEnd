using RedeStore.Application.Common;
using RedeStore.Domain.Entities;
using RedeStore.UnitTests.Eventos;

namespace RedeStore.UnitTests.Inscricoes;

public sealed class FakeInscricaoRepository : IInscricaoRepository
{
    private readonly Dictionary<Guid, Evento> _eventosPorId;
    private readonly Dictionary<Guid, Inscricao> _inscricoesPorId = new();

    public FakeInscricaoRepository(FakeEventoRepository eventoRepository)
    {
        _eventosPorId = eventoRepository.EventosPorId;
    }

    public Task<Inscricao?> BuscarConfirmadaPorEventoEUsuarioAsync(Guid eventoId, Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.Values.SingleOrDefault(
            i => i.EventoId == eventoId && i.UsuarioId == usuarioId && i.Status == StatusInscricao.Confirmada));

    public Task<Inscricao?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.GetValueOrDefault(id));

    public Task<List<Inscricao>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.Values.Where(i => i.UsuarioId == usuarioId).ToList());

    public Task<List<Inscricao>> ListarPorEventoAsync(Guid eventoId, CancellationToken ct) =>
        Task.FromResult(_inscricoesPorId.Values.Where(i => i.EventoId == eventoId).ToList());

    public Task AdicionarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _inscricoesPorId[inscricao.Id] = inscricao;
        if (_eventosPorId.TryGetValue(inscricao.EventoId, out var evento))
        {
            evento.Inscricoes.Add(inscricao);
        }
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Inscricao inscricao, CancellationToken ct)
    {
        _inscricoesPorId[inscricao.Id] = inscricao;
        return Task.CompletedTask;
    }
}
