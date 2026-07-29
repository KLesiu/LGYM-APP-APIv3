using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class EmailNotificationSubscriptionRepository : IEmailNotificationSubscriptionRepository
{
    private readonly INotificationsPersistenceContext _persistenceContext;

    public EmailNotificationSubscriptionRepository(INotificationsPersistenceContext persistenceContext)
    {
        _persistenceContext = persistenceContext;
    }

    public Task<bool> IsSubscribedAsync(Id<User> userId, string notificationType, CancellationToken cancellationToken = default)
    {
        return _persistenceContext.EmailNotificationSubscriptions
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId && x.NotificationType == notificationType,
                cancellationToken);
    }
}
