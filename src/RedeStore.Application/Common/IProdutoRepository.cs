using RedeStore.Domain.Entities;

namespace RedeStore.Application.Common;

public interface IProdutoRepository
{
    Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct);
    Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct);
    Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct);
    Task AdicionarAsync(Produto produto, CancellationToken ct);
    Task AtualizarAsync(Produto produto, CancellationToken ct);
    Task RemoverAsync(Produto produto, CancellationToken ct);
}
