namespace RedeStore.Application.Common;

public interface IUnitOfWork
{
    Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct);
}
