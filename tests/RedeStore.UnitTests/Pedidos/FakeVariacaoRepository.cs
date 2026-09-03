using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Pedidos;

public sealed class FakeVariacaoRepository : IVariacaoRepository
{
    private readonly IProdutoRepository _produtoRepository;

    public FakeVariacaoRepository(IProdutoRepository produtoRepository)
    {
        _produtoRepository = produtoRepository;
    }

    public async Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct)
    {
        var produtos = await _produtoRepository.ListarAsync(null, null, ct);
        var variacao = produtos.SelectMany(p => p.Variacoes).SingleOrDefault(v => v.Id == variacaoId);

        if (variacao is null || variacao.Estoque < quantidade)
        {
            return false;
        }

        variacao.Estoque -= quantidade;
        return true;
    }
}
