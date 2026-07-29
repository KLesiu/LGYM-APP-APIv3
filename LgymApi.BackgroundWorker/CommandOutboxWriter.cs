using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Runtime;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker;

public sealed class CommandOutboxWriter : ICommandOutboxWriter
{
    private readonly IBackgroundActionResolver _backgroundActionResolver;
    private readonly CommandContractRegistry _commandContractRegistry;
    private readonly ICommandEnvelopeRuntime _commandEnvelopeRuntime;
    private readonly ILogger<CommandOutboxWriter> _logger;

    public CommandOutboxWriter(
        IBackgroundActionResolver backgroundActionResolver,
        CommandContractRegistry commandContractRegistry,
        ICommandEnvelopeRuntime commandEnvelopeRuntime,
        ILogger<CommandOutboxWriter> logger)
    {
        _backgroundActionResolver = backgroundActionResolver;
        _commandContractRegistry = commandContractRegistry;
        _commandEnvelopeRuntime = commandEnvelopeRuntime;
        _logger = logger;
    }

    public async Task<CommandEnvelopeStageResult> StageAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : class, IActionCommand
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = typeof(TCommand);
        var handlerCount = _backgroundActionResolver.GetHandlerTypeNames(commandType).Count;
        if (handlerCount == 0)
        {
            _logger.LogWarning(
                "No handlers registered for command type {CommandType}. Skipping durable envelope staging.",
                commandType.FullName);
            return new CommandEnvelopeStageResult(null, false);
        }

        var request = CommandEnvelopeFactory.Create(command, _commandContractRegistry);
        var receipt = await _commandEnvelopeRuntime.StageAsync(request, cancellationToken);

        if (receipt.WasExisting)
        {
            _logger.LogInformation(
                "Command envelope already exists for correlation {CorrelationId} (envelope {EnvelopeId}).",
                request.CommandId,
                receipt.EnvelopeId);
        }
        else
        {
            _logger.LogInformation(
                "Staged command {CommandId} with correlation {CorrelationId}.",
                request.CommandId,
                receipt.EnvelopeId);
        }

        return new CommandEnvelopeStageResult(receipt.EnvelopeId, receipt.WasExisting);
    }
}
