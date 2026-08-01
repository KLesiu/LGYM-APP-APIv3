using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.TraineeNotes.History;

internal sealed class GetTraineeNoteHistoryUseCase : IGetTraineeNoteHistoryUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly IMapper _mapper;

    public GetTraineeNoteHistoryUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ICoachingTraineeNotePersistence notes,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _notes = notes;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> ExecuteAsync(
        GetTraineeNoteHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = TraineeNoteRules.GetAccessError(access, query.TraineeId);
        if (accessError is not null)
        {
            return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Failure(accessError);
        }

        if (query.NoteId.IsEmpty)
        {
            return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Failure(
                new BadRequestError(Messages.FieldRequired));
        }

        var note = await _notes.FindNoteByIdAsync(query.NoteId, cancellationToken);
        if (note is null || !TraineeNoteRules.IsOwnedBy(note, query.TrainerId, query.TraineeId))
        {
            return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Failure(
                new NotFoundError(Messages.DidntFind));
        }

        var history = await _notes.GetNoteHistoryAsync(note.Id, cancellationToken);
        var readModels = _mapper.MapList<CoachingTraineeNoteHistoryFact, TraineeNoteHistoryReadModel>(
            history,
            _mapper.CreateContext());
        return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Success(readModels);
    }
}
