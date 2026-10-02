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
        var grupo = app.MapGroup("/pedidos").WithTags("Pedidos");

        grupo.MapPost("/", async (CriarPedidoRequest request, ClaimsPrincipal user, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var pedido = await pedidoService.CriarAsync(usuarioId, request, ct);
            return Results.Ok(pedido);
        })
        .RequireAuthorization()
        .AddEndpointFilter<ValidationFilter<CriarPedidoRequest>>()
        .WithSummary("Cria um pedido (checkout) para o usuário autenticado")
        .WithDescription("Regras: ao menos 1 item (produtoId, tamanho, cor obrigatórios; quantidade > 0); formaEntrega 'retirada' ou 'entrega'; endereco obrigatório quando 'entrega' (rua, numero, bairro, cidade, cep). Debita o estoque de forma atômica; o pedido nasce com status 'pago'. Erros: 404 PRODUTO_NAO_ENCONTRADO / VARIACAO_NAO_ENCONTRADA, 409 ESTOQUE_INSUFICIENTE.")
        .Produces<PedidoDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapGet("/", async (IPedidoService pedidoService, CancellationToken ct) =>
        {
            var pedidos = await pedidoService.ListarTodosAsync(ct);
            return Results.Ok(pedidos);
        })
        .RequireAuthorization("Admin")
        .WithSummary("Lista todos os pedidos (admin, mais recentes primeiro)")
        .Produces<List<PedidoDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        grupo.MapPatch("/{id:guid}/avancar-status", async (Guid id, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var pedido = await pedidoService.AvancarStatusAsync(id, ct);
            return Results.Ok(pedido);
        })
        .RequireAuthorization("Admin")
        .WithSummary("Avança o status do pedido para a próxima etapa (admin)")
        .WithDescription("Sem corpo. Fluxo: pago -> em_preparo -> retirado (formaEntrega 'retirada') ou entregue (formaEntrega 'entrega'). Pedido já em estado final retorna 409 PEDIDO_EM_ESTADO_FINAL.")
        .Produces<PedidoDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/usuarios/me/pedidos", async (ClaimsPrincipal user, IPedidoService pedidoService, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var pedidos = await pedidoService.ListarPorUsuarioAsync(usuarioId, ct);
            return Results.Ok(pedidos);
        })
        .RequireAuthorization()
        .WithTags("Pedidos")
        .WithSummary("Lista os pedidos do usuário autenticado (mais recentes primeiro)")
        .Produces<List<PedidoDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
