using LgymApi.BackgroundWorker.Common.Notifications;

namespace LgymApi.Application.Notifications.Contracts.Email;

public interface IEmailJobExecutionPort
{
    Task ProcessAsync(string notificationId, CancellationToken cancellationToken = default);
}

public interface IEmailSchedulingPort<TPayload>
    where TPayload : IEmailPayload
{
    Task ScheduleAsync(TPayload payload, CancellationToken cancellationToken = default);
}

public sealed record TrainingCompletedEmailExercise(string ExerciseId, string ExerciseName, int Series, double Reps, double Weight, string Unit);

public sealed record TrainingCompletedEmailDeliveryRequest(string UserId, string TrainingId, string RecipientEmail, string CultureName, string PreferredTimeZone, string PlanDayName, DateTimeOffset TrainingDate, IReadOnlyList<TrainingCompletedEmailExercise> Exercises);

public interface ITrainingCompletedEmailDeliveryPort
{
    Task DeliverAsync(TrainingCompletedEmailDeliveryRequest request, CancellationToken cancellationToken = default);
}

public sealed record WelcomeEmailDeliveryRequest(string UserId, string UserName, string RecipientEmail, string CultureName);

public interface IWelcomeEmailDeliveryPort
{
    Task DeliverAsync(WelcomeEmailDeliveryRequest request, CancellationToken cancellationToken = default);
}

public sealed record InvitationAcceptedEmailDeliveryRequest(string InvitationId, string TrainerId, string TraineeId, string TrainerEmail, string TrainerCultureName, string TrainerTimeZone, string TrainerName, string TraineeName);

public interface IInvitationAcceptedEmailDeliveryPort
{
    Task DeliverAsync(InvitationAcceptedEmailDeliveryRequest request, CancellationToken cancellationToken = default);
}

public sealed record InvitationRevokedEmailDeliveryRequest(string InvitationId, string TrainerId, string InviteeEmail, string TrainerEmail, string TrainerCultureName, string TrainerTimeZone, string TrainerName);

public interface IInvitationRevokedEmailDeliveryPort
{
    Task DeliverAsync(InvitationRevokedEmailDeliveryRequest request, CancellationToken cancellationToken = default);
}

public sealed record InvitationCreatedEmailDeliveryRequest(
    string InvitationId,
    string TrainerId,
    string? TraineeId,
    string InviteeEmail,
    string InvitationCode,
    DateTimeOffset ExpiresAt,
    string TrainerName,
    string TrainerEmail,
    string TrainerCultureName,
    string TrainerTimeZone,
    string? TraineeName,
    string? TraineeEmail,
    string? TraineeTimeZone);

public interface IInvitationCreatedEmailDeliveryPort
{
    Task DeliverAsync(InvitationCreatedEmailDeliveryRequest request, CancellationToken cancellationToken = default);
}
