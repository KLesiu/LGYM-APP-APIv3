namespace LgymApi.Application.Platform.Contracts.BackgroundCommands;

public interface ICommandDispatcher
{
    Task EnqueueAsync<TCommand>(TCommand command)
        where TCommand : class, IActionCommand;
}
