using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Api.Filters;
using RedeStore.Application.Pedidos;
using RedeStore.Application.Pedidos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class PedidosEndpoints
{
    public static void MapPedidosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/pedidos");

        grupo.MapPost("/", async (CriarPedidoRequest request, ClaimsPrincipal user, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var pedido = await pedidoService.CriarAsync(usuarioId, request, ct);
            return Results.Ok(pedido);
        }).RequireAuthorization().AddEndpointFilter<ValidationFilter<CriarPedidoRequest>>();

        grupo.MapGet("/", async (IPedidoService pedidoService, CancellationToken ct) =>
        {
            var pedidos = await pedidoService.ListarTodosAsync(ct);
            return Results.Ok(pedidos);
        }).RequireAuthorization("Admin");

        grupo.MapPatch("/{id:guid}/avancar-status", async (Guid id, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var pedido = await pedidoService.AvancarStatusAsync(id, ct);
            return Results.Ok(pedido);
        }).RequireAuthorization("Admin");

        app.MapGet("/usuarios/me/pedidos", async (ClaimsPrincipal user, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var pedidos = await pedidoService.ListarPorUsuarioAsync(usuarioId, ct);
            return Results.Ok(pedidos);
        }).RequireAuthorization();
    }
}
