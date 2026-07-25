using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Coaching.TraineeNotes.TrainerList;

internal sealed class ListTrainerNotesUseCase : IListTrainerNotesUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly IMapper _mapper;

    public ListTrainerNotesUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ICoachingTraineeNotePersistence notes,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _notes = notes;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> ExecuteAsync(
        ListTrainerNotesQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = TraineeNoteRules.GetAccessError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<IReadOnlyList<TraineeNoteReadModel>, AppError>.Failure(accessError);
        }

        var notes = await _notes.GetNotesByTrainerAndTraineeAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var readModels = _mapper.MapList<CoachingTraineeNoteFact, TraineeNoteReadModel>(
            notes,
            _mapper.CreateContext());
        return Result<IReadOnlyList<TraineeNoteReadModel>, AppError>.Success(readModels);
    }
}
