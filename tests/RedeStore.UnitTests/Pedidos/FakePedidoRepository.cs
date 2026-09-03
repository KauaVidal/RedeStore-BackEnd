using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Pedidos;

public sealed class FakePedidoRepository : IPedidoRepository
{
    private readonly Dictionary<Guid, Pedido> _pedidosPorId = new();

    public Task<List<Pedido>> ListarTodosAsync(CancellationToken ct) =>
        Task.FromResult(_pedidosPorId.Values.OrderByDescending(p => p.CriadoEm).ToList());

    public Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct) =>
        Task.FromResult(_pedidosPorId.Values.Where(p => p.UsuarioId == usuarioId)
            .OrderByDescending(p => p.CriadoEm).ToList());

    public Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_pedidosPorId.GetValueOrDefault(id));

    public Task AdicionarAsync(Pedido pedido, CancellationToken ct)
    {
        _pedidosPorId[pedido.Id] = pedido;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Pedido pedido, CancellationToken ct)
    {
        _pedidosPorId[pedido.Id] = pedido;
        return Task.CompletedTask;
    }
}
