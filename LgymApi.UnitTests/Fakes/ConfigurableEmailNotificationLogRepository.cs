using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.UnitTests.Fakes;

internal sealed class ConfigurableEmailNotificationLogRepository : IEmailNotificationLogRepository
{
    public List<(string Method, object? Argument, CancellationToken CancellationToken)> Calls { get; } = [];
    public Func<NotificationMessage, CancellationToken, Task> Add { get; set; } = (_, _) => Task.CompletedTask;
    public Func<Id<NotificationMessage>, CancellationToken, Task<NotificationMessage?>> FindById { get; set; } = (_, _) => Task.FromResult<NotificationMessage?>(null);
    public Func<EmailNotificationType, Id<CorrelationScope>, string, CancellationToken, Task<NotificationMessage?>> FindByCorrelation { get; set; } = (_, _, _, _) => Task.FromResult<NotificationMessage?>(null);
    public Func<CancellationToken, Task<List<NotificationMessage>>> GetPendingUndispatched { get; set; } = _ => Task.FromResult(new List<NotificationMessage>());
    public Func<CancellationToken, Task<List<NotificationMessage>>> GetFailed { get; set; } = _ => Task.FromResult(new List<NotificationMessage>());
    public Func<CancellationToken, Task<List<NotificationMessage>>> GetDeadLettered { get; set; } = _ => Task.FromResult(new List<NotificationMessage>());
    public Func<EmailNotificationStatus, CancellationToken, Task<int>> CountByStatus { get; set; } = (_, _) => Task.FromResult(0);
    public Func<DateTimeOffset, CancellationToken, Task<int>> DeleteSentOlderThan { get; set; } = (_, _) => Task.FromResult(0);
    public Func<Id<NotificationMessage>, CancellationToken, Task<bool>> TryTransitionToSending { get; set; } = (_, _) => Task.FromResult(false);
    public Func<int, CancellationToken, Task<List<NotificationMessage>>> GetStuckSending { get; set; } = (_, _) => Task.FromResult(new List<NotificationMessage>());

    public Task AddAsync(NotificationMessage message, CancellationToken cancellationToken = default) { Calls.Add((nameof(AddAsync), message, cancellationToken)); return Add(message, cancellationToken); }
    public Task<NotificationMessage?> FindByIdAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByIdAsync), id, cancellationToken)); return FindById(id, cancellationToken); }
    public Task<NotificationMessage?> FindByCorrelationAsync(EmailNotificationType type, Id<CorrelationScope> correlationId, string recipient, CancellationToken cancellationToken = default) { Calls.Add((nameof(FindByCorrelationAsync), (type, correlationId, recipient), cancellationToken)); return FindByCorrelation(type, correlationId, recipient, cancellationToken); }
    public Task<List<NotificationMessage>> GetPendingUndispatchedAsync(CancellationToken cancellationToken = default) { Calls.Add((nameof(GetPendingUndispatchedAsync), null, cancellationToken)); return GetPendingUndispatched(cancellationToken); }
    public Task<List<NotificationMessage>> GetFailedAsync(CancellationToken cancellationToken = default) { Calls.Add((nameof(GetFailedAsync), null, cancellationToken)); return GetFailed(cancellationToken); }
    public Task<List<NotificationMessage>> GetDeadLetteredAsync(CancellationToken cancellationToken = default) { Calls.Add((nameof(GetDeadLetteredAsync), null, cancellationToken)); return GetDeadLettered(cancellationToken); }
    public Task<int> CountByStatusAsync(EmailNotificationStatus status, CancellationToken cancellationToken = default) { Calls.Add((nameof(CountByStatusAsync), status, cancellationToken)); return CountByStatus(status, cancellationToken); }
    public Task<int> DeleteSentOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default) { Calls.Add((nameof(DeleteSentOlderThanAsync), cutoffDate, cancellationToken)); return DeleteSentOlderThan(cutoffDate, cancellationToken); }
    public Task<bool> TryTransitionToSendingAsync(Id<NotificationMessage> id, CancellationToken cancellationToken = default) { Calls.Add((nameof(TryTransitionToSendingAsync), id, cancellationToken)); return TryTransitionToSending(id, cancellationToken); }
    public Task<List<NotificationMessage>> GetStuckSendingAsync(int emailSendLeaseSeconds, CancellationToken cancellationToken = default) { Calls.Add((nameof(GetStuckSendingAsync), emailSendLeaseSeconds, cancellationToken)); return GetStuckSending(emailSendLeaseSeconds, cancellationToken); }
}
