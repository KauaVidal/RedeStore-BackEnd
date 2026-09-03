namespace RedeStore.Application.Common;

public interface IVariacaoRepository
{
    Task<bool> DecrementarEstoqueAsync(Guid variacaoId, int quantidade, CancellationToken ct);
}
