namespace RedeStore.Application.Produtos.Dtos;

public sealed record ProdutoDto(
    Guid Id,
    string Nome,
    string Categoria,
    decimal Preco,
    string Descricao,
    List<string> Fotos,
    List<string> Tamanhos,
    List<string> Cores,
    bool Destaque,
    List<VariacaoDto> Variacoes);
