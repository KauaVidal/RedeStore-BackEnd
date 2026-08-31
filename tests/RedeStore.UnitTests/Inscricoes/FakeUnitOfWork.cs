using RedeStore.Application.Common;

namespace RedeStore.UnitTests.Inscricoes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct) =>
        Task.FromResult<ITransacao>(new FakeTransacao());

    private sealed class FakeTransacao : ITransacao
    {
        public Task ConfirmarAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
