using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Resources;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;

internal sealed class GetVisibleTraineeNoteUseCase : IGetVisibleTraineeNoteUseCase
{
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly IMapper _mapper;

    public GetVisibleTraineeNoteUseCase(
        ICoachingTraineeNotePersistence notes,
        IMapper mapper)
    {
        _notes = notes;
        _mapper = mapper;
    }

    public async Task<Result<TraineeNoteReadModel, AppError>> ExecuteAsync(
        GetVisibleTraineeNoteQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.NoteId.IsEmpty)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(
                new BadRequestError(Messages.FieldRequired));
        }

        var note = await _notes.FindNoteByIdAsync(query.NoteId, cancellationToken);
        if (note is null || note.TraineeId != query.TraineeId || !note.VisibleToTrainee)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(
                new NotFoundError(Messages.DidntFind));
        }

        var readModel = _mapper.Map<CoachingTraineeNoteFact, TraineeNoteReadModel>(
            note,
            _mapper.CreateContext());
        return Result<TraineeNoteReadModel, AppError>.Success(readModel);
    }
}
