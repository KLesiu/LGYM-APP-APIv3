namespace LgymApi.Application.Platform.Contracts.BackgroundCommands;

public interface ICommandOutboxWriter
{
    Task<CommandEnvelopeStageResult> StageAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class, IActionCommand;
}

public sealed record CommandEnvelopeStageResult(string? EnvelopeId, bool WasExisting);
