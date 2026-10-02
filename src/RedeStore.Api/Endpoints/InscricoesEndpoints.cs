using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using RedeStore.Application.Inscricoes;
using RedeStore.Application.Inscricoes.Dtos;

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
        })
        .RequireAuthorization()
        .WithTags("Inscrições")
        .WithSummary("Inscreve o usuário autenticado no evento")
        .WithDescription("Sem corpo. Sempre 200 quando o evento existe; o campo 'resultado' indica o desfecho: 'criada' (nova inscrição confirmada), 'ja_inscrito' (devolve a inscrição existente) ou 'esgotado' (inscricao = null). valorPago = preço do evento no momento da inscrição.")
        .Produces<ResultadoInscricaoDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/usuarios/me/inscricoes", async (ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var inscricoes = await inscricaoService.ListarPorUsuarioAsync(idUsuarioLogado, ct);
            return Results.Ok(inscricoes);
        })
        .RequireAuthorization()
        .WithTags("Inscrições")
        .WithSummary("Lista as inscrições do usuário autenticado (mais recentes primeiro)")
        .WithDescription("Inclui inscrições confirmadas e canceladas.")
        .Produces<List<InscricaoDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapGet("/eventos/{eventoId:guid}/inscricoes", async (Guid eventoId, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var inscricoes = await inscricaoService.ListarPorEventoAsync(eventoId, ct);
            return Results.Ok(inscricoes);
        })
        .RequireAuthorization("Admin")
        .WithTags("Inscrições")
        .WithSummary("Lista as inscrições de um evento (admin, mais recentes primeiro)")
        .WithDescription("Evento inexistente retorna lista vazia (não 404).")
        .Produces<List<InscricaoDto>>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapPatch("/inscricoes/{id:guid}/cancelar", async (Guid id, ClaimsPrincipal user, IInscricaoService inscricaoService, CancellationToken ct) =>
        {
            var idUsuarioLogado = Guid.Parse(user.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
            var ehAdmin = user.IsInRole("admin");
            var inscricao = await inscricaoService.CancelarAsync(id, idUsuarioLogado, ehAdmin, ct);
            return Results.Ok(inscricao);
        })
        .RequireAuthorization()
        .WithTags("Inscrições")
        .WithSummary("Cancela uma inscrição (dono da inscrição ou admin)")
        .WithDescription("Sem corpo. Muda o status para 'cancelada', liberando a vaga. Não há reembolso automático.")
        .Produces<InscricaoDto>()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
