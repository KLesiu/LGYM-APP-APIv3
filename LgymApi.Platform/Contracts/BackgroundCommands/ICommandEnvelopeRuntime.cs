namespace LgymApi.Application.Platform.Contracts.BackgroundCommands;

public interface ICommandEnvelopeRuntime
{
    Task<CommandEnvelopeReceipt> PersistAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default);

    Task<CommandEnvelopeReceipt> StageAsync(CommandEnvelopeRequest request, CancellationToken cancellationToken = default);

    Task<CommandEnvelopeStart> BeginAsync(string envelopeId, CancellationToken cancellationToken = default);

    Task<CommandEnvelopeFinalization> FinalizeAsync(
        string envelopeId,
        int attemptNumber,
        IReadOnlyList<CommandHandlerResult> results,
        CancellationToken cancellationToken = default);

    Task RecordFaultAsync(string envelopeId, string reason, string errorMessage, string errorDetails, CancellationToken cancellationToken = default);

    Task RecordCancellationAsync(string envelopeId);
}

public sealed record CommandEnvelopeRequest(string CommandId, string PayloadJson);

public sealed record CommandEnvelopeReceipt(string? EnvelopeId, bool WasExisting);

public sealed record CommandEnvelopeStart(
    string State,
    string? CommandId,
    string? PayloadJson,
    int AttemptNumber);

public sealed record CommandHandlerResult(
    bool Success,
    string HandlerTypeName,
    string? ErrorMessage,
    string? ErrorDetails);

public sealed record CommandEnvelopeFinalization(
    bool Retry,
    string? NextAttemptAt,
    bool DeadLettered,
    string? ErrorMessage);
