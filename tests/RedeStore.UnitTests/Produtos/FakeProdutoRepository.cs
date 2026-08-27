using RedeStore.Application.Common;
using RedeStore.Domain.Entities;

namespace RedeStore.UnitTests.Produtos;

public sealed class FakeProdutoRepository : IProdutoRepository
{
    private readonly Dictionary<Guid, Produto> _produtosPorId = new();

    public Task<List<Produto>> ListarAsync(CategoriaProduto? categoria, string? busca, CancellationToken ct)
    {
        var query = _produtosPorId.Values.AsEnumerable();

        if (categoria is not null)
        {
            query = query.Where(p => p.Categoria == categoria);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            query = query.Where(p => p.Nome.Contains(busca, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(query.ToList());
    }

    public Task<List<Produto>> ListarDestaquesAsync(CancellationToken ct) =>
        Task.FromResult(_produtosPorId.Values.Where(p => p.Destaque).ToList());

    public Task<Produto?> BuscarPorIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_produtosPorId.GetValueOrDefault(id));

    public Task AdicionarAsync(Produto produto, CancellationToken ct)
    {
        _produtosPorId[produto.Id] = produto;
        return Task.CompletedTask;
    }

    public Task AtualizarAsync(Produto produto, CancellationToken ct)
    {
        _produtosPorId[produto.Id] = produto;
        return Task.CompletedTask;
    }

    public Task RemoverAsync(Produto produto, CancellationToken ct)
    {
        _produtosPorId.Remove(produto.Id);
        return Task.CompletedTask;
    }
}
