using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Api.Endpoints;

public static class UsuariosEndpoints
{
    public static void MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/usuarios/{id:guid}", async (Guid id, ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var ehAdmin = user.IsInRole("admin");
            var usuario = await authService.ObterComAutorizacaoAsync(id, idUsuarioLogado, ehAdmin, ct);
            return Results.Ok(usuario);
        })
        .RequireAuthorization()
        .WithTags("Usuários")
        .WithSummary("Retorna um usuário pelo id")
        .WithDescription("O próprio usuário pode consultar o seu id; admin pode consultar qualquer um. Demais casos retornam 403 ACESSO_NEGADO.")
        .Produces<UsuarioDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);
    }
}
