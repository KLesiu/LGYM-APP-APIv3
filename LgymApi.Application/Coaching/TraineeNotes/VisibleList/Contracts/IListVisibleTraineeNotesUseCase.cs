using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleList;

public interface IListVisibleTraineeNotesUseCase
{
    Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> ExecuteAsync(
        ListVisibleTraineeNotesQuery query,
        CancellationToken cancellationToken = default);
}
