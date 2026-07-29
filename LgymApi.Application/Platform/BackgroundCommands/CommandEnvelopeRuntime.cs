using System.Security.Cryptography;
using System.Text;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
namespace LgymApi.Application.Platform.BackgroundCommands;

internal sealed class CommandEnvelopeRuntime(
    ICommandEnvelopeRepository repository,
    IUnitOfWork unitOfWork) : ICommandEnvelopeRuntime
{
    public Task<CommandEnvelopeReceipt> StageAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default) =>
        AddOrGetAsync(request, commit: false, cancellationToken);

    public Task<CommandEnvelopeReceipt> PersistAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default) =>
        AddOrGetAsync(request, commit: true, cancellationToken);

    public async Task<CommandEnvelopeStart> BeginAsync(string envelopeId, CancellationToken cancellationToken = default)
    {
        if (!Id<CommandEnvelope>.TryParse(envelopeId, out var id))
        {
            throw new ArgumentException("Command envelope ID is invalid.", nameof(envelopeId));
        }

        var envelope = await repository.FindByIdAsync(id, cancellationToken);
        if (envelope is null)
        {
            return new CommandEnvelopeStart("not-found", null, null, 0);
        }

        if (envelope.Status == ActionExecutionStatus.Completed)
        {
            return new CommandEnvelopeStart("completed", null, null, 0);
        }

        if (envelope.Status == ActionExecutionStatus.DeadLettered)
        {
            return new CommandEnvelopeStart("dead-lettered", null, null, 0);
        }

        if (envelope.Status == ActionExecutionStatus.Processing)
        {
            return new CommandEnvelopeStart("processing", null, null, 0);
        }

        var attempt = envelope.GetExecutionAttemptCount();
        envelope.Status = ActionExecutionStatus.Processing;
        envelope.LastAttemptAt = DateTimeOffset.UtcNow;
        envelope.ProcessingStartedAtUtc = DateTimeOffset.UtcNow;
        envelope.NextAttemptAt = null;
        envelope.ExecutionLogs.Add(new ActionExecutionLog
        {
            Id = Id<ActionExecutionLog>.New(),
            CommandEnvelopeId = envelope.Id,
            ActionType = ActionExecutionLogType.Execute,
            Status = ActionExecutionStatus.Processing,
            AttemptNumber = attempt
        });
        await repository.UpdateAsync(envelope, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CommandEnvelopeStart("running", envelope.CommandTypeFullName, envelope.PayloadJson, attempt);
    }

    public async Task<CommandEnvelopeFinalization> FinalizeAsync(
        string envelopeId,
        int attemptNumber,
        IReadOnlyList<CommandHandlerResult> results,
        CancellationToken cancellationToken = default)
    {
        var envelope = await FindRequiredAsync(envelopeId, cancellationToken);
        var attemptLog = envelope.ExecutionLogs.Last(log => log.ActionType == ActionExecutionLogType.Execute && log.AttemptNumber == attemptNumber);
        foreach (var result in results)
        {
            envelope.ExecutionLogs.Add(new ActionExecutionLog
            {
                Id = Id<ActionExecutionLog>.New(),
                CommandEnvelopeId = envelope.Id,
                ActionType = ActionExecutionLogType.HandlerExecution,
                Status = result.Success ? ActionExecutionStatus.Completed : ActionExecutionStatus.Failed,
                AttemptNumber = attemptNumber,
                HandlerTypeName = result.HandlerTypeName,
                ErrorMessage = result.ErrorMessage,
                ErrorDetails = result.ErrorDetails
            });
        }

        var failures = results.Where(result => !result.Success).ToList();
        if (failures.Count == 0)
        {
            attemptLog.Status = ActionExecutionStatus.Completed;
            envelope.MarkCompleted();
            await repository.UpdateAsync(envelope, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new CommandEnvelopeFinalization(false, null, false, null);
        }

        var errorMessage = string.Join("; ", failures.Select(result => result.ErrorMessage));
        var errorDetails = string.Join(Environment.NewLine + Environment.NewLine, failures
            .Where(result => !string.IsNullOrWhiteSpace(result.ErrorDetails))
            .Select(result => result.ErrorDetails));
        attemptLog.Status = ActionExecutionStatus.Failed;
        attemptLog.ErrorMessage = errorMessage;
        attemptLog.ErrorDetails = string.IsNullOrWhiteSpace(errorDetails) ? null : errorDetails;
        envelope.RecordAttemptFailure(errorMessage);
        await repository.UpdateAsync(envelope, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (envelope.ShouldRetry())
        {
            return new CommandEnvelopeFinalization(true, envelope.NextAttemptAt?.ToString("O"), false, errorMessage);
        }

        envelope.MarkDeadLettered("Dead-lettered after maximum retry attempts exceeded", string.IsNullOrWhiteSpace(errorDetails) ? errorMessage : errorDetails);
        await repository.UpdateAsync(envelope, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CommandEnvelopeFinalization(false, null, true, errorMessage);
    }

    public async Task RecordFaultAsync(string envelopeId, string reason, string errorMessage, string errorDetails, CancellationToken cancellationToken = default)
    {
        var envelope = await FindRequiredAsync(envelopeId, cancellationToken);
        var attemptLog = envelope.ExecutionLogs.Last(log => log.ActionType == ActionExecutionLogType.Execute && log.Status == ActionExecutionStatus.Processing);
        attemptLog.Status = ActionExecutionStatus.Failed;
        attemptLog.ErrorMessage = errorMessage;
        attemptLog.ErrorDetails = errorDetails;
        envelope.MarkDeadLettered(reason, errorDetails);
        await repository.UpdateAsync(envelope, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordCancellationAsync(string envelopeId)
    {
        var envelope = await FindRequiredAsync(envelopeId, CancellationToken.None);
        const string message = "Command orchestration was cancelled.";
        var attemptLog = envelope.ExecutionLogs.Last(log => log.ActionType == ActionExecutionLogType.Execute && log.Status == ActionExecutionStatus.Processing);
        attemptLog.Status = ActionExecutionStatus.Failed;
        attemptLog.ErrorMessage = message;
        attemptLog.ErrorDetails = message;
        envelope.RecordAttemptFailure(message, message);
        await repository.UpdateAsync(envelope, CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<CommandEnvelopeReceipt> AddOrGetAsync(
        CommandEnvelopeRequest request,
        bool commit,
        CancellationToken cancellationToken)
    {
        var envelope = new CommandEnvelope
        {
            Id = Id<CommandEnvelope>.New(),
            CorrelationId = CreateCorrelationId(request.CommandId, request.PayloadJson),
            CommandTypeFullName = request.CommandId,
            PayloadJson = request.PayloadJson,
            Status = ActionExecutionStatus.Pending,
            NextAttemptAt = DateTimeOffset.UtcNow
        };
        var winner = await repository.AddOrGetExistingAsync(envelope, cancellationToken);
        if (!ReferenceEquals(winner, envelope))
        {
            return new CommandEnvelopeReceipt(winner.Id.ToString(), true);
        }

        if (!commit)
        {
            return new CommandEnvelopeReceipt(envelope.Id.ToString(), false);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            var recoveredEnvelope = await repository.TryRecoverDuplicateAsync(envelope, exception, cancellationToken);
            if (recoveredEnvelope is null)
            {
                throw;
            }

            return new CommandEnvelopeReceipt(recoveredEnvelope.Id.ToString(), true);
        }

        return new CommandEnvelopeReceipt(envelope.Id.ToString(), false);
    }

    private static Id<CorrelationScope> CreateCorrelationId(string commandId, string payloadJson)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{commandId}|{payloadJson}"));
        return Id<CorrelationScope>.FromBytes(hash[..16]);
    }

    private async Task<CommandEnvelope> FindRequiredAsync(string envelopeId, CancellationToken cancellationToken)
    {
        if (!Id<CommandEnvelope>.TryParse(envelopeId, out var id))
        {
            throw new ArgumentException("Command envelope ID is invalid.", nameof(envelopeId));
        }

        return await repository.FindByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Command envelope {envelopeId} was not found.");
    }
}
