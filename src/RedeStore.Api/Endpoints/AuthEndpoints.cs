using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Api.Filters;
using RedeStore.Application.Auth;
using RedeStore.Application.Auth.Dtos;

namespace RedeStore.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/auth");

        grupo.MapPost("/cadastro", async (CadastroRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var resposta = await authService.CadastrarAsync(request, ct);
            return Results.Ok(resposta);
        }).AddEndpointFilter<ValidationFilter<CadastroRequest>>();

        grupo.MapPost("/login", async (LoginRequest request, IAuthService authService, CancellationToken ct) =>
        {
            var resposta = await authService.LoginAsync(request, ct);
            return Results.Ok(resposta);
        }).AddEndpointFilter<ValidationFilter<LoginRequest>>();

        grupo.MapGet("/me", async (ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var usuario = await authService.ObterPorIdAsync(id, ct);
            return Results.Ok(usuario);
        }).RequireAuthorization();

        grupo.MapPatch("/perfil", async (AtualizarPerfilRequest request, ClaimsPrincipal user, IAuthService authService, CancellationToken ct) =>
        {
            var id = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var usuario = await authService.AtualizarPerfilAsync(id, request, ct);
            return Results.Ok(usuario);
        }).RequireAuthorization().AddEndpointFilter<ValidationFilter<AtualizarPerfilRequest>>();

        grupo.MapPost("/recuperar-senha", async (RecuperarSenhaRequest request, IAuthService authService, CancellationToken ct) =>
        {
            await authService.RecuperarSenhaAsync(request.Email, ct);
            return Results.NoContent();
        }).AddEndpointFilter<ValidationFilter<RecuperarSenhaRequest>>();

        grupo.MapPost("/redefinir-senha", async (RedefinirSenhaRequest request, IAuthService authService, CancellationToken ct) =>
        {
            await authService.RedefinirSenhaAsync(request.Token, request.NovaSenha, ct);
            return Results.NoContent();
        }).AddEndpointFilter<ValidationFilter<RedefinirSenhaRequest>>();
    }
}
