using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Pedidos;

public sealed class FakeVariacaoRepository : IVariacaoRepository
{
    private readonly IProdutoRepository _produtoRepository;

    public FakeVariacaoRepository(IProdutoRepository produtoRepository)
    {
        _produtoRepository = produtoRepository;
    }

    /// <summary>
    /// Ids das variações, na ordem em que <see cref="DecrementarEstoqueAsync"/> foi chamado.
    /// Usado por testes para provar que o decremento segue uma ordem determinística
    /// (por Variacao.Id) e não a ordem em que os itens chegaram no request.
    /// </summary>
    public List<Guid> OrdemDeChamadas { get; } = new();

    public async Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct)
    {
        OrdemDeChamadas.Add(variacaoId);

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
