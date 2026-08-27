namespace RedeStore.Application.Produtos.Dtos;

public sealed record CriarProdutoRequest(
    string Nome,
    string Categoria,
    decimal Preco,
    string Descricao,
    List<string>? Fotos,
    bool Destaque,
    List<VariacaoRequest> Variacoes);
