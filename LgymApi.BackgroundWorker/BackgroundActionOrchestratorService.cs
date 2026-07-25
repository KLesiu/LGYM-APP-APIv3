using System.Text.Json;
using LgymApi.Application.Repositories;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.BackgroundWorker.Runtime;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RuntimeCommandDescriptor = LgymApi.BackgroundWorker.Runtime.CommandDescriptor;

namespace LgymApi.BackgroundWorker;

/// <summary>
/// Core orchestrator handler for background action commands.
/// Loads durable command envelope, resolves exact-type handlers, executes actions in parallel with isolated scopes.
/// Implements continue-on-error, retry/backoff, and dead-letter policies.
/// </summary>
public sealed partial class BackgroundActionOrchestratorService
{
    private readonly IBackgroundActionResolver _backgroundActionResolver;
    private readonly CommandContractRegistry _commandContractRegistry;
    private readonly ICommandEnvelopeRepository _commandEnvelopeRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BackgroundActionOrchestratorService> _logger;

    // Parallel execution configuration
    private const int MaxDegreeOfParallelism = 4;

    public BackgroundActionOrchestratorService(
        IBackgroundActionResolver backgroundActionResolver,
        CommandContractRegistry commandContractRegistry,
        ICommandEnvelopeRepository commandEnvelopeRepository,
        IUnitOfWork unitOfWork,
        ILogger<BackgroundActionOrchestratorService> logger)
    {
        _backgroundActionResolver = backgroundActionResolver;
        _commandContractRegistry = commandContractRegistry;
        _commandEnvelopeRepository = commandEnvelopeRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Orchestrates background action execution for a single command envelope.
    /// </summary>
    /// <param name="envelopeId">Durable command envelope id</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task OrchestrateAsync(Id<CommandEnvelope> envelopeId, CancellationToken cancellationToken = default)
    {
        // Load envelope with execution logs
        var envelope = await _commandEnvelopeRepository.FindByIdAsync(envelopeId, cancellationToken);
        if (envelope == null)
        {
            _logger.LogWarning(
                "Command envelope {EnvelopeId} not found. Skipping orchestration.",
                envelopeId);
            return;
        }

        // Skip if already terminal
        if (envelope.Status == ActionExecutionStatus.Completed)
        {
            _logger.LogInformation(
                "Command envelope {EnvelopeId} already completed. Skipping duplicate orchestration.",
                envelopeId);
            return;
        }

        if (envelope.Status == ActionExecutionStatus.DeadLettered)
        {
            _logger.LogWarning(
                "Command envelope {EnvelopeId} is dead-lettered. No further processing.",
                envelopeId);
            return;
        }

        if (envelope.Status == ActionExecutionStatus.Processing)
        {
            _logger.LogInformation(
                "Command envelope {EnvelopeId} is already processing. Skipping duplicate redelivery.",
                envelopeId);
            return;
        }

        var currentAttemptNumber = envelope.GetExecutionAttemptCount();
        var envelopeAttemptLog = new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = envelope.Id,
            ActionType = ActionExecutionLogType.Execute,
            Status = ActionExecutionStatus.Processing,
            AttemptNumber = currentAttemptNumber,
            HandlerTypeName = null,
            ErrorMessage = null,
            ErrorDetails = null
        };

        // Update status to Processing
        envelope.Status = ActionExecutionStatus.Processing;
        envelope.LastAttemptAt = DateTimeOffset.UtcNow;
        envelope.ProcessingStartedAtUtc = DateTimeOffset.UtcNow;
        envelope.NextAttemptAt = null;
        envelope.ExecutionLogs.Add(envelopeAttemptLog);
        await _commandEnvelopeRepository.UpdateAsync(envelope, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Resolve command runtime type from durable descriptor
        RuntimeCommandDescriptor descriptor;
        try
        {
            descriptor = _commandContractRegistry.Resolve(envelope.CommandTypeFullName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to resolve command ID for envelope {EnvelopeId}. Marking dead-lettered.",
                envelopeId);

            await MarkDeadLetteredAsync(
                envelope,
                envelopeAttemptLog,
                "Dead-lettered because command ID could not be resolved",
                ex,
                cancellationToken);
            return;
        }

        var commandType = descriptor.RuntimeType;

        // Deserialize command payload
        object command;
        try
        {
            command = JsonSerializer.Deserialize(envelope.PayloadJson, commandType, SharedSerializationOptions.Current)
                      ?? throw new InvalidOperationException("Deserialized command is null.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to deserialize command payload for envelope {EnvelopeId}. Marking dead-lettered.",
                envelopeId);

            await MarkDeadLetteredAsync(
                envelope,
                envelopeAttemptLog,
                "Dead-lettered because command payload could not be deserialized",
                ex,
                cancellationToken);
            return;
        }

        var handlerTypeNames = _backgroundActionResolver.GetHandlerTypeNames(commandType);
        var handlerCount = handlerTypeNames.Count;

        if (handlerCount == 0)
        {
            _logger.LogWarning(
                "No handlers registered for command {CommandId} (envelope {EnvelopeId}). Completing without execution.",
                descriptor.CanonicalId,
                envelopeId);

            envelopeAttemptLog.Status = ActionExecutionStatus.Completed;
            envelope.MarkCompleted();
            await _commandEnvelopeRepository.UpdateAsync(envelope, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        _logger.LogInformation(
            "Orchestrating {HandlerCount} handler(s) for command {CommandId} (envelope {EnvelopeId}).",
            handlerCount,
            descriptor.CanonicalId,
            envelopeId);

        var executionTasks = new List<Task<HandlerExecutionResult>>();
        using var semaphore = new SemaphoreSlim(MaxDegreeOfParallelism);
        HandlerExecutionResult[] results;
        try
        {
            for (int i = 0; i < handlerCount; i++)
            {
                var expectedHandlerTypeName = handlerTypeNames[i];
                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await ExecuteHandlerInIsolatedScopeAsync(
                            command,
                            commandType,
                            descriptor.CanonicalId,
                            expectedHandlerTypeName,
                            cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);
                executionTasks.Add(task);
            }

            results = await Task.WhenAll(executionTasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RecordCancelledAttemptAsync(envelope, envelopeAttemptLog);
            throw;
        }
        var hasFailures = results.Any(r => !r.Success);
        foreach (var result in results)
        {
            var executionLog = new ActionExecutionLog
            {
                Id = Id<ActionExecutionLog>.New(),
                CommandEnvelopeId = envelope.Id,
                ActionType = ActionExecutionLogType.HandlerExecution,
                Status = result.Success ? ActionExecutionStatus.Completed : ActionExecutionStatus.Failed,
                AttemptNumber = currentAttemptNumber,
                HandlerTypeName = result.HandlerTypeName,
                ErrorMessage = result.Success ? null : result.ErrorMessage,
                ErrorDetails = result.Success ? null : result.ErrorDetails
            };
            envelope.ExecutionLogs.Add(executionLog);
        }

        if (hasFailures)
        {
            var errorMessages = results.Where(r => !r.Success).Select(r => r.ErrorMessage).ToList();
            var combinedError = string.Join("; ", errorMessages);
            var combinedErrorDetails = string.Join(
                Environment.NewLine + Environment.NewLine,
                results
                    .Where(r => !r.Success && !string.IsNullOrWhiteSpace(r.ErrorDetails))
                    .Select(r => r.ErrorDetails));

            envelopeAttemptLog.Status = ActionExecutionStatus.Failed;
            envelopeAttemptLog.ErrorMessage = combinedError;
            envelopeAttemptLog.ErrorDetails = string.IsNullOrWhiteSpace(combinedErrorDetails) ? null : combinedErrorDetails;

            envelope.RecordAttemptFailure(combinedError);
            await _commandEnvelopeRepository.UpdateAsync(envelope, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (envelope.ShouldRetry())
            {
                _logger.LogWarning(
                    "Envelope {EnvelopeId} has failures. Retry attempt {AttemptNumber}/{MaxAttempts}. Hangfire will retry after {NextAttemptAt}.",
                    envelopeId,
                    currentAttemptNumber + 1,
                    CommandEnvelope.MaxRetryAttempts,
                    envelope.NextAttemptAt);

                // Throw exception to trigger Hangfire AutomaticRetry (60/300/900s delays)
                // This ensures backoff delays are enforced at the job level
                throw new InvalidOperationException(
                    $"Envelope {envelopeId} handler execution failed. Retry scheduled at {envelope.NextAttemptAt}. Error: {combinedError}");
            }
            else
            {
                _logger.LogError(
                    "Envelope {EnvelopeId} exceeded max retry attempts. Marking dead-lettered.",
                    envelopeId);

                envelope.MarkDeadLettered(
                    "Dead-lettered after maximum retry attempts exceeded",
                    string.IsNullOrWhiteSpace(combinedErrorDetails) ? combinedError : combinedErrorDetails);
                await _commandEnvelopeRepository.UpdateAsync(envelope, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            // All handlers succeeded
            envelopeAttemptLog.Status = ActionExecutionStatus.Completed;
            envelope.MarkCompleted();
            await _commandEnvelopeRepository.UpdateAsync(envelope, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Envelope {EnvelopeId} completed successfully.",
                envelopeId);
        }
    }
}
