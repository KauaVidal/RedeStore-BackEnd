using RedeStore.Api.Filters;
using RedeStore.Application.Produtos;
using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Api.Endpoints;

public static class ProdutosEndpoints
{
    public static void MapProdutosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/produtos").WithTags("Produtos");

        grupo.MapGet("/", async (string? categoria, string? busca, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produtos = await produtoService.ListarAsync(categoria, busca, ct);
            return Results.Ok(produtos);
        })
        .WithSummary("Lista produtos, com filtro opcional por categoria e busca por nome")
        .WithDescription("categoria: 'camisetas', 'moletons' ou 'acessorios' (valor desconhecido é ignorado e não filtra). busca: trecho do nome, sem diferenciar maiúsculas/minúsculas.")
        .Produces<List<ProdutoDto>>();

        grupo.MapGet("/destaques", async (IProdutoService produtoService, CancellationToken ct) =>
        {
            var produtos = await produtoService.ListarDestaquesAsync(ct);
            return Results.Ok(produtos);
        })
        .WithSummary("Lista os produtos marcados como destaque")
        .Produces<List<ProdutoDto>>();

        grupo.MapGet("/{id:guid}", async (Guid id, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.ObterPorIdAsync(id, ct);
            return Results.Ok(produto);
        })
        .WithSummary("Retorna um produto pelo id")
        .Produces<ProdutoDto>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapPost("/", async (CriarProdutoRequest request, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.CriarAsync(request, ct);
            return Results.Ok(produto);
        })
        .RequireAuthorization("Admin")
        .AddEndpointFilter<ValidationFilter<CriarProdutoRequest>>()
        .WithSummary("Cria um produto (admin)")
        .WithDescription("Regras: nome e descrição obrigatórios, categoria em 'camisetas'|'moletons'|'acessorios', preço > 0 e ao menos 1 variação (tamanho e cor obrigatórios, estoque >= 0). 'tamanhos' e 'cores' da resposta são derivados das variações.")
        .Produces<ProdutoDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        grupo.MapPatch("/{id:guid}", async (Guid id, AtualizarProdutoRequest request, IProdutoService produtoService, CancellationToken ct) =>
        {
            var produto = await produtoService.AtualizarAsync(id, request, ct);
            return Results.Ok(produto);
        })
        .RequireAuthorization("Admin")
        .AddEndpointFilter<ValidationFilter<AtualizarProdutoRequest>>()
        .WithSummary("Atualiza parcialmente um produto (admin)")
        .WithDescription("Só os campos enviados (não nulos) são alterados. Se 'variacoes' for enviado, SUBSTITUI todas as variações existentes (novos ids são gerados).")
        .Produces<ProdutoDto>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        grupo.MapDelete("/{id:guid}", async (Guid id, IProdutoService produtoService, CancellationToken ct) =>
        {
            await produtoService.RemoverAsync(id, ct);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithSummary("Remove um produto (admin)")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
