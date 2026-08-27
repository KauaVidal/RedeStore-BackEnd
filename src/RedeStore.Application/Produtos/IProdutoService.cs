using RedeStore.Application.Produtos.Dtos;

namespace RedeStore.Application.Produtos;

public interface IProdutoService
{
    Task<List<ProdutoDto>> ListarAsync(string? categoria, string? busca, CancellationToken ct);
    Task<List<ProdutoDto>> ListarDestaquesAsync(CancellationToken ct);
    Task<ProdutoDto> ObterPorIdAsync(Guid id, CancellationToken ct);
    Task<ProdutoDto> CriarAsync(CriarProdutoRequest request, CancellationToken ct);
    Task<ProdutoDto> AtualizarAsync(Guid id, AtualizarProdutoRequest request, CancellationToken ct);
    Task RemoverAsync(Guid id, CancellationToken ct);
}
