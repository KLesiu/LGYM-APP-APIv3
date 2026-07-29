namespace LgymApi.Application.Repositories;

public interface ICommittedIntentDispatcher
{
    Task DispatchCommittedIntentsAsync(CancellationToken cancellationToken = default);
}
