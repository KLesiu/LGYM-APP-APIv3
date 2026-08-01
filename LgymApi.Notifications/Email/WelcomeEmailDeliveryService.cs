using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.BackgroundWorker.Common.Notifications.Models;

namespace LgymApi.Application.Notifications.Email;

internal sealed class WelcomeEmailDeliveryService(IEmailSchedulingPort<WelcomeEmailPayload> scheduler) : IWelcomeEmailDeliveryPort
{
    public async Task DeliverAsync(WelcomeEmailDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.TryParse(request.UserId, out var userId)) return;
        await scheduler.ScheduleAsync(new WelcomeEmailPayload { UserId = userId, UserName = request.UserName, RecipientEmail = request.RecipientEmail, CultureName = request.CultureName }, cancellationToken);
    }
}
