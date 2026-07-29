using LgymApi.Application.Repositories;

namespace LgymApi.IntegrationTests;

internal sealed class ObservedUnitOfWork(
    IUnitOfWork inner,
    Func<Task>? beforeSave = null) : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }
    public int BeginTransactionCalls { get; private set; }
    public int CommitCalls { get; private set; }
    public int RollbackCalls { get; private set; }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        if (beforeSave is not null)
        {
            await beforeSave();
        }

        return await inner.SaveChangesAsync(cancellationToken);
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        BeginTransactionCalls++;
        var transaction = await inner.BeginTransactionAsync(cancellationToken);
        return new ObservedTransaction(transaction, this);
    }

    private sealed class ObservedTransaction(
        IUnitOfWorkTransaction innerTransaction,
        ObservedUnitOfWork owner) : IUnitOfWorkTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            owner.CommitCalls++;
            await innerTransaction.CommitAsync(cancellationToken);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            owner.RollbackCalls++;
            await innerTransaction.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => innerTransaction.DisposeAsync();
    }
}
