using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;

namespace LgymApi.Application.Coaching.TraineeNotes.Delete;

internal sealed class DeleteTraineeNoteUseCase : IDeleteTraineeNoteUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public DeleteTraineeNoteUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ICoachingTraineeNotePersistence notes,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _notes = notes;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = TraineeNoteRules.GetAccessError(access, command.TraineeId);
        if (accessError is not null)
        {
            return Result<Unit, AppError>.Failure(accessError);
        }

        if (command.NoteId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new BadRequestError(Messages.FieldRequired));
        }

        var existing = await _notes.FindNoteByIdAsync(command.NoteId, cancellationToken);
        if (existing is null || !TraineeNoteRules.IsOwnedBy(existing, command.TrainerId, command.TraineeId))
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        var deleted = _mapper.Map<DeleteTraineeNoteSource, CoachingTraineeNoteWriteModel>(
            new DeleteTraineeNoteSource(existing, command.TrainerId, DateTimeOffset.UtcNow),
            _mapper.CreateContext());
        await _notes.UpdateNoteAsync(deleted, cancellationToken);

        var history = _mapper.Map<TraineeNoteHistorySource, CoachingTraineeNoteHistoryWriteModel>(
            new TraineeNoteHistorySource(
                Id<TraineeNoteHistoryEntity>.New(),
                deleted.Id,
                command.TrainerId,
                DateTimeOffset.UtcNow,
                existing.Content,
                existing.Content,
                "Deleted",
                existing.VisibleToTrainee,
                false),
            _mapper.CreateContext());
        await _notes.AddHistoryEntryAsync(history, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
