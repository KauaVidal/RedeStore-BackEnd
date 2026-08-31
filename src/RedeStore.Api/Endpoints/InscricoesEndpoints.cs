using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Application.Inscricoes;

namespace RedeStore.Api.Endpoints;

public static class InscricoesEndpoints
{
    public static void MapInscricoesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/eventos/{eventoId:guid}/inscricoes", async (Guid eventoId, ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var resultado = await inscricaoService.InscreverAsync(eventoId, idUsuarioLogado, ct);
            return Results.Ok(resultado);
        }).RequireAuthorization();

        app.MapGet("/usuarios/me/inscricoes", async (ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var inscricoes = await inscricaoService.ListarPorUsuarioAsync(idUsuarioLogado, ct);
            return Results.Ok(inscricoes);
        }).RequireAuthorization();

        app.MapGet("/eventos/{eventoId:guid}/inscricoes", async (Guid eventoId, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var inscricoes = await inscricaoService.ListarPorEventoAsync(eventoId, ct);
            return Results.Ok(inscricoes);
        }).RequireAuthorization("Admin");

        app.MapPatch("/inscricoes/{id:guid}/cancelar", async (Guid id, ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var ehAdmin = user.IsInRole("admin");
            var inscricao = await inscricaoService.CancelarAsync(id, idUsuarioLogado, ehAdmin, ct);
            return Results.Ok(inscricao);
        }).RequireAuthorization();
    }
}
