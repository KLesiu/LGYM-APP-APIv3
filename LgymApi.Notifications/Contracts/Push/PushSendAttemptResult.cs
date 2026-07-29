namespace LgymApi.Application.Notifications.Contracts.Push;

public enum PushSendOutcome
{
    Sent = 0,
    TransientFailure = 1,
    InvalidToken = 2,
    PermanentFailure = 3,
    Skipped = 4
}

public sealed record PushSendAttemptResult(
    PushSendOutcome Outcome,
    string ProviderStatus,
    string? ProviderMessageId,
    string? ProviderErrorCode,
    string? ProviderResponseSummary);
