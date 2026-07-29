using LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed partial class UpdateTrainingMainRecordsHandler(ITrainingMainRecordsUpdatePort port) : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<TrainingCompletedCommand>
{
    public Task ExecuteAsync(TrainingCompletedCommand command, CancellationToken cancellationToken = default) =>
        port.UpdateAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
}
