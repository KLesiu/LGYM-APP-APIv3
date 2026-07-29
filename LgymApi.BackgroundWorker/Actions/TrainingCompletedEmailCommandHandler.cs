using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed partial class TrainingCompletedEmailCommandHandler(
    ITrainingCompletedEmailPreparationPort preparationPort,
    ITrainingCompletedEmailDeliveryPort deliveryPort) : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<TrainingCompletedCommand>
{
    public async Task ExecuteAsync(TrainingCompletedCommand command, CancellationToken cancellationToken = default)
    {
        var preparation = await preparationPort.PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
        if (preparation is null) return;

        await deliveryPort.DeliverAsync(new TrainingCompletedEmailDeliveryRequest(
            preparation.UserId, preparation.TrainingId, preparation.RecipientEmail, preparation.CultureName,
            preparation.PreferredTimeZone, preparation.PlanDayName, preparation.TrainingDate,
            preparation.Exercises.Select(exercise => new TrainingCompletedEmailExercise(
                exercise.ExerciseId, exercise.ExerciseName, exercise.Series, exercise.Reps, exercise.Weight, exercise.Unit)).ToList()), cancellationToken);
    }
}
