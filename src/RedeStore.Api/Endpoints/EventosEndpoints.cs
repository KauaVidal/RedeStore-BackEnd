using RedeStore.Api.Filters;
using RedeStore.Application.Eventos;
using RedeStore.Application.Eventos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class EventosEndpoints
{
    public static void MapEventosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/eventos").WithTags("Eventos");

        grupo.MapGet("/", async (bool? apenasFuturos, IEventoService eventoService, CancellationToken ct) =>
        {
            var eventos = await eventoService.ListarAsync(apenasFuturos ?? false, ct);
            return Results.Ok(eventos);
        })
        .WithSummary("Lista eventos")
        .WithDescription("apenasFuturos=true retorna só eventos com dataHora >= agora (UTC). Padrão: false (todos).")
        .Produces<List<EventoDto>>();

        grupo.MapGet("/{id:guid}", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.ObterPorIdAsync(id, ct);
            return Results.Ok(evento);
        })
        .WithSummary("Retorna um evento pelo id")
        .Produces<EventoDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapGet("/{id:guid}/vagas-restantes", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            var vagas = await eventoService.ObterVagasRestantesAsync(id, ct);
            return Results.Ok(vagas);
        })
        .WithSummary("Retorna quantas vagas restam no evento")
        .WithDescription("vagasRestantes = vagasTotais - inscrições com status 'confirmada'.")
        .Produces<VagasRestantesDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPost("/", async (CriarEventoRequest request, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.CriarAsync(request, ct);
            return Results.Ok(evento);
        })
        .RequireAuthorization("Admin")
        .AddEndpointFilter<ValidationFilter<CriarEventoRequest>>()
        .WithSummary("Cria um evento (admin)")
        .WithDescription("Regras: titulo, descricao, local e foto obrigatórios; preco >= 0; vagasTotais >= 1.")
        .Produces<EventoDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        grupo.MapPatch("/{id:guid}", async (Guid id, AtualizarEventoRequest request, IEventoService eventoService, CancellationToken ct) =>
        {
            var evento = await eventoService.AtualizarAsync(id, request, ct);
            return Results.Ok(evento);
        })
        .RequireAuthorization("Admin")
        .AddEndpointFilter<ValidationFilter<AtualizarEventoRequest>>()
        .WithSummary("Atualiza parcialmente um evento (admin)")
        .WithDescription("Só os campos enviados (não nulos) são alterados. Reduzir vagasTotais abaixo do número de inscrições confirmadas retorna 409 EVENTO_VAGAS_TOTAIS_INSUFICIENTES.")
        .Produces<EventoDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        grupo.MapDelete("/{id:guid}", async (Guid id, IEventoService eventoService, CancellationToken ct) =>
        {
            await eventoService.RemoverAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithSummary("Remove um evento (admin)")
        .WithDescription("Só é possível remover eventos sem inscrições confirmadas; caso contrário retorna 409 EVENTO_COM_INSCRICOES_CONFIRMADAS.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
