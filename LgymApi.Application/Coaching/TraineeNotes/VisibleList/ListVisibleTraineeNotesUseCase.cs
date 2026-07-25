using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleList;

internal sealed class ListVisibleTraineeNotesUseCase : IListVisibleTraineeNotesUseCase
{
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly IMapper _mapper;

    public ListVisibleTraineeNotesUseCase(
        ICoachingTraineeNotePersistence notes,
        IMapper mapper)
    {
        _notes = notes;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> ExecuteAsync(
        ListVisibleTraineeNotesQuery query,
        CancellationToken cancellationToken = default)
    {
        var notes = await _notes.GetVisibleNotesByTraineeAsync(query.TraineeId, cancellationToken);
        var readModels = _mapper.MapList<CoachingTraineeNoteFact, TraineeNoteReadModel>(
            notes,
            _mapper.CreateContext());
        return Result<IReadOnlyList<TraineeNoteReadModel>, AppError>.Success(readModels);
    }
}
