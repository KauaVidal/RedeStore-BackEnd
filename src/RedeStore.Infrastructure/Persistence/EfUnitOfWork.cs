using Microsoft.EntityFrameworkCore.Storage;
using RedeStore.Application.Common;

namespace RedeStore.Infrastructure.Persistence;

public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly RedeStoreDbContext _dbContext;

    public EfUnitOfWork(RedeStoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ITransacao> IniciarTransacaoAsync(CancellationToken ct)
    {
        var transacaoEfCore = await _dbContext.Database.BeginTransactionAsync(ct);
        return new EfTransacao(transacaoEfCore);
    }

    private sealed class EfTransacao : ITransacao
    {
        private readonly IDbContextTransaction _transacao;

        public EfTransacao(IDbContextTransaction transacao)
        {
            _transacao = transacao;
        }

        public Task ConfirmarAsync(CancellationToken ct) => _transacao.CommitAsync(ct);

        public async ValueTask DisposeAsync() => await _transacao.DisposeAsync();
    }
}
