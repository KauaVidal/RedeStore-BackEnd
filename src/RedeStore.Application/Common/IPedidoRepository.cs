using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IPedidoRepository
{
    Task<List<Pedido>> ListarTodosAsync(CancellationToken ct);
    Task<List<Pedido>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<Pedido?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Pedido pedido, CancellationToken ct);
    Task AtualizarAsync(Pedido pedido, CancellationToken ct);
}
