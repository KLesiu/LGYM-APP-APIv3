using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Platform.Contracts.Serialization;

namespace LgymApi.Application.Coaching.Contracts.Notifications
{
    public sealed record TraineeNoteUpdatedInAppPreparation(
        string TraineeNoteId,
        string TraineeId,
        string TrainerId,
        string? NoteTitle,
        DateTimeOffset TriggeredAt,
        string? TrainerName,
        string? TrainerEmail,
        string? TrainerCultureName,
        string? TrainerTimeZone,
        string? TraineeName,
        string? TraineeEmail,
        string? TraineeCultureName,
        string? TraineeTimeZone);

    public interface ITraineeNoteUpdatedInAppPreparationPort
    {
        Task<TraineeNoteUpdatedInAppPreparation> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Coaching.Notifications
{
    internal sealed class TraineeNoteUpdatedInAppPreparationPort(
        IAccountReadService accountReadService) : ITraineeNoteUpdatedInAppPreparationPort
    {
        public async Task<TraineeNoteUpdatedInAppPreparation> PrepareAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            var command = JsonSerializer.Deserialize<TraineeNoteUpdatedInAppNotificationCommand>(payloadJson, SharedSerializationOptions.Current)
                ?? throw new InvalidOperationException("Trainee note updated action payload is invalid.");
            var trainer = await accountReadService.GetByIdAsync(command.TrainerId, cancellationToken);
            var trainee = await accountReadService.GetByIdAsync(command.TraineeId, cancellationToken);
            return new TraineeNoteUpdatedInAppPreparation(
                command.TraineeNoteId.ToString(),
                command.TraineeId.ToString(),
                command.TrainerId.ToString(),
                command.NoteTitle,
                command.TriggeredAt,
                trainer?.Name,
                trainer?.Email,
                trainer?.PreferredLanguage,
                trainer?.PreferredTimeZone,
                trainee?.Name,
                trainee?.Email,
                trainee?.PreferredLanguage,
                trainee?.PreferredTimeZone);
        }
    }
}
