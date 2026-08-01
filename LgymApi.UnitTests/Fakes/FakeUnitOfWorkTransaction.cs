using LgymApi.Application.Repositories;

namespace LgymApi.TestUtils.Fakes;

public sealed class FakeUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    public int CommitCalls { get; private set; }
    public int RollbackCalls { get; private set; }
    public bool IsDisposed { get; private set; }
    public Exception? CommitException { get; set; }
    public Exception? RollbackException { get; set; }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        CommitCalls++;

        if (CommitException is not null)
        {
            return Task.FromException(CommitException);
        }

        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RollbackCalls++;

        if (RollbackException is not null)
        {
            return Task.FromException(RollbackException);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}
