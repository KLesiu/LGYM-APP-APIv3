using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.BackgroundWorker.Common.Notifications.Models;

namespace LgymApi.Notifications;

internal sealed class PasswordRecoveryEmailSchedulerAdapter(
    IEmailSchedulingPort<PasswordRecoveryEmailPayload> scheduler) : IPasswordRecoveryEmailScheduler
{
    public Task ScheduleAsync(PasswordRecoveryEmailRequest request, CancellationToken cancellationToken = default)
    {
        return scheduler.ScheduleAsync(new PasswordRecoveryEmailPayload
        {
            UserId = request.UserId,
            TokenId = request.TokenId,
            UserName = request.UserName,
            RecipientEmail = request.RecipientEmail,
            ResetToken = request.ResetToken,
            ResetUrl = request.ResetUrl,
            CultureName = request.CultureName
        }, cancellationToken);
    }
}
