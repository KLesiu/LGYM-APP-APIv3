namespace LgymApi.Application.Features.PasswordReset.Contracts;

public interface IPasswordRecoveryEmailScheduler
{
    Task ScheduleAsync(
        PasswordRecoveryEmailRequest request,
        CancellationToken cancellationToken = default);
}
