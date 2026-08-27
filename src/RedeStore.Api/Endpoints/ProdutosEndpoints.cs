using RedeStore.Api.Filters;
using RedeStore.Application.Produtos;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class ProdutosEndpoints
{
    public static void MapProdutosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/produtos");

        grupo.MapGet("/", async (string? categoria, string? busca, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produtos = await produtoService.ListarAsync(categoria, busca, ct);
            return Results.Ok(produtos);
        });

        grupo.MapGet("/destaques", async (IProdutoService produtoService, CancellationToken ct) =>
        {
            var produtos = await produtoService.ListarDestaquesAsync(ct);
            return Results.Ok(produtos);
        });

        grupo.MapGet("/{id:guid}", async (Guid id, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.ObterPorIdAsync(id, ct);
            return Results.Ok(produto);
        });

        grupo.MapPost("/", async (CriarProdutoRequest request, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.CriarAsync(request, ct);
            return Results.Ok(produto);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<CriarProdutoRequest>>();

        grupo.MapPatch("/{id:guid}", async (Guid id, AtualizarProdutoRequest request, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.AtualizarAsync(id, request, ct);
            return Results.Ok(produto);
        }).RequireAuthorization("Admin").AddEndpointFilter<ValidationFilter<AtualizarProdutoRequest>>();

        grupo.MapDelete("/{id:guid}", async (Guid id, IProdutoService produtoService, CancellationToken ct) =>
        {
            await produtoService.RemoverAsync(id, ct);
            return Results.NoContent();
        }).RequireAuthorization("Admin");
    }
}
