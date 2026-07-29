using LgymApi.Application.Repositories;

namespace LgymApi.TestUtils.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public FakeUnitOfWork()
    {
    }

    public FakeUnitOfWork(FakeUnitOfWorkTransaction transaction)
    {
        Transaction = transaction;
    }

    public int SaveChangesCalls { get; private set; }
    public int BeginTransactionCalls { get; private set; }
    public int SaveChangesResult { get; set; } = 1;
    public Exception? BeginTransactionException { get; set; }
    public Exception? SaveChangesException { get; set; }
    public FakeUnitOfWorkTransaction? Transaction { get; set; } = new();
    public List<object> DetachedEntities { get; } = new();

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;

        if (SaveChangesException is not null)
        {
            return Task.FromException<int>(SaveChangesException);
        }

        return Task.FromResult(SaveChangesResult);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        BeginTransactionCalls++;

        if (BeginTransactionException is not null)
        {
            return Task.FromException<IUnitOfWorkTransaction>(BeginTransactionException);
        }

        if (Transaction is null)
        {
            return Task.FromException<IUnitOfWorkTransaction>(new NotSupportedException("Transactions are not configured for this fake unit of work."));
        }

        return Task.FromResult<IUnitOfWorkTransaction>(Transaction);
    }

    public void DetachEntity<TEntity>(TEntity entity) where TEntity : class
    {
        DetachedEntities.Add(entity);
    }
}
