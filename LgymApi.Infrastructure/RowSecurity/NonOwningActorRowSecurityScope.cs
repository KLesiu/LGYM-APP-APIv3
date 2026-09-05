using LgymApi.Application.Repositories;

namespace LgymApi.Infrastructure.RowSecurity;

internal sealed class NonOwningActorRowSecurityScope : IUnitOfWorkTransaction
{
    internal static readonly NonOwningActorRowSecurityScope Instance = new();

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
