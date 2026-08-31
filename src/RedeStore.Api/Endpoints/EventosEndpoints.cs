using RedeStore.Api.Filters;
using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class EventosEndpoints
{
    public static void MapEventosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/eventos");

        grupo.MapGet("/", async (bool? apenasFuturos, IEventoService eventoService, CancellationToken ct) =>
        {
            var eventos = await eventoService.ListarAsync(apenasFuturos ?? false, ct);
            return Results.Ok(eventos);
        });

        grupo.MapGet("/{id:guid}", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.ObterPorIdAsync(id, ct);
            return Results.Ok(evento);
        });

        grupo.MapGet("/{id:guid}/vagas-restantes", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            var vagas = await eventoService.ObterVagasRestantesAsync(id, ct);
            return Results.Ok(vagas);
        });

        grupo.MapPost("/", async (CriarEventoRequest request, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.CriarAsync(request, ct);
            return Results.Ok(evento);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<CriarEventoRequest>>();

        grupo.MapPatch("/{id:guid}", async (Guid id, AtualizarEventoRequest request, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.AtualizarAsync(id, request, ct);
            return Results.Ok(evento);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<AtualizarEventoRequest>>();

        grupo.MapDelete("/{id:guid}", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            await eventoService.RemoverAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }
}
