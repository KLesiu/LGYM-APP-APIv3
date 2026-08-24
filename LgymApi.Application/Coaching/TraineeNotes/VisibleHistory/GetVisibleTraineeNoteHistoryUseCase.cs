using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleHistory;

internal sealed class GetVisibleTraineeNoteHistoryUseCase : IGetVisibleTraineeNoteHistoryUseCase
{
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly IMapper _mapper;

    public GetVisibleTraineeNoteHistoryUseCase(
        ICoachingTraineeNotePersistence notes,
        IMapper mapper)
    {
        _notes = notes;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> ExecuteAsync(
        GetVisibleTraineeNoteHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.TraineeId.IsEmpty || query.NoteId.IsEmpty)
        {
            return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Failure(
                new BadRequestError(Messages.FieldRequired));
        }

        var note = await _notes.FindNoteByIdAsync(query.NoteId, cancellationToken);
        if (note is null || note.TraineeId != query.TraineeId || !note.VisibleToTrainee)
        {
            return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Failure(
                new NotFoundError(Messages.DidntFind));
        }

        var history = await _notes.GetVisibleNoteHistoryAsync(note.Id, cancellationToken);
        return Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>.Success(
            _mapper.MapList<CoachingTraineeNoteHistoryFact, TraineeNoteHistoryReadModel>(
                history,
                _mapper.CreateContext()));
    }
}
