using RedeStore.Application.Pedidos.Dtos;

namespace RedeStore.Application.Pedidos;

public interface IPedidoService
{
    Task<PedidoDto> CriarAsync(Guid usuarioId, CriarPedidoRequest request, CancellationToken ct);
    Task<List<PedidoDto>> ListarPorUsuarioAsync(Guid usuarioId, CancellationToken ct);
    Task<List<PedidoDto>> ListarTodosAsync(CancellationToken ct);
    Task<PedidoDto> AvancarStatusAsync(Guid id, CancellationToken ct);
}
