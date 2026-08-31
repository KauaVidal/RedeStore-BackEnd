namespace RedeStore.Application.Common;

public interface ITransacao : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken ct);
}
