using System.Text.Json;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Runtime;
using Microsoft.Extensions.Logging;

namespace LgymApi.BackgroundWorker;

public sealed partial class BackgroundActionOrchestratorService
{
    private readonly IBackgroundActionResolver _backgroundActionResolver;
    private readonly CommandContractRegistry _commandContractRegistry;
    private readonly ICommandEnvelopeRuntime _commandEnvelopeRuntime;
    private readonly ILogger<BackgroundActionOrchestratorService> _logger;

    private const int MaxDegreeOfParallelism = 4;

    public BackgroundActionOrchestratorService(
        IBackgroundActionResolver backgroundActionResolver,
        CommandContractRegistry commandContractRegistry,
        ICommandEnvelopeRuntime commandEnvelopeRuntime,
        ILogger<BackgroundActionOrchestratorService> logger)
    {
        _backgroundActionResolver = backgroundActionResolver;
        _commandContractRegistry = commandContractRegistry;
        _commandEnvelopeRuntime = commandEnvelopeRuntime;
        _logger = logger;
    }

    public async Task OrchestrateAsync(string envelopeId, CancellationToken cancellationToken = default)
    {
        var start = await _commandEnvelopeRuntime.BeginAsync(envelopeId, cancellationToken);
        if (start.State != "running")
        {
            _logger.LogInformation("Command envelope {EnvelopeId} skipped with state {State}.", envelopeId, start.State);
            return;
        }

        Runtime.CommandDescriptor descriptor;
        object command;
        try
        {
            descriptor = _commandContractRegistry.Resolve(start.CommandId!);
            command = JsonSerializer.Deserialize(start.PayloadJson!, descriptor.RuntimeType, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException("Deserialized command is null.");
        }
        catch (Exception exception)
        {
            var reason = exception.Message.Contains("Unknown durable command", StringComparison.Ordinal)
                ? "Dead-lettered because command ID could not be resolved"
                : "Dead-lettered because command payload could not be deserialized";
            await _commandEnvelopeRuntime.RecordFaultAsync(envelopeId, reason, exception.Message, exception.ToString(), cancellationToken);
            return;
        }

        var handlerTypeNames = _backgroundActionResolver.GetHandlerTypeNames(descriptor.RuntimeType);
        if (handlerTypeNames.Count == 0)
        {
            await _commandEnvelopeRuntime.FinalizeAsync(envelopeId, start.AttemptNumber, [], cancellationToken);
            return;
        }

        CommandHandlerResult[] results;
        try
        {
            results = await ExecuteHandlersAsync(command, descriptor.RuntimeType, descriptor.CanonicalId, handlerTypeNames, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _commandEnvelopeRuntime.RecordCancellationAsync(envelopeId);
            throw;
        }

        var finalization = await _commandEnvelopeRuntime.FinalizeAsync(envelopeId, start.AttemptNumber, results, cancellationToken);
        if (finalization.Retry)
        {
            throw new InvalidOperationException($"Envelope {envelopeId} handler execution failed. Retry scheduled at {finalization.NextAttemptAt}. Error: {finalization.ErrorMessage}");
        }
    }
}
