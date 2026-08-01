using LgymApi.Application.Platform.Contracts.BackgroundCommands;

namespace LgymApi.BackgroundWorker.Actions.Contracts;

public interface IBackgroundAction<in TCommand>
    where TCommand : IActionCommand
{
    Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
