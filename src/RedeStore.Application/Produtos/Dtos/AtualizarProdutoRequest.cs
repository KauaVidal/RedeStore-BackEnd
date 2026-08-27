namespace RedeStore.Application.Produtos.Dtos;

public sealed record AtualizarProdutoRequest(
    string? Nome,
    string? Categoria,
    decimal? Preco,
    string? Descricao,
    List<string>? Fotos,
    bool? Destaque,
    List<VariacaoRequest>? Variacoes);
