using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;

namespace LgymApi.Application.Coaching.TraineeNotes.Update;

internal sealed class UpdateTraineeNoteUseCase : IUpdateTraineeNoteUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UpdateTraineeNoteUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ICoachingTraineeNotePersistence notes,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _notes = notes;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<TraineeNoteReadModel, AppError>> ExecuteAsync(
        UpdateTraineeNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        var upsertError = TraineeNoteRules.GetUpsertError(command.Data);
        if (upsertError is not null)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(upsertError);
        }

        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = TraineeNoteRules.GetAccessError(access, command.TraineeId);
        if (accessError is not null)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(accessError);
        }

        if (command.NoteId.IsEmpty)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(new BadRequestError(Messages.FieldRequired));
        }

        var existing = await _notes.FindNoteByIdAsync(command.NoteId, cancellationToken);
        if (existing is null || !TraineeNoteRules.IsOwnedBy(existing, command.TrainerId, command.TraineeId))
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        var updated = _mapper.Map<UpdateTraineeNoteSource, CoachingTraineeNoteWriteModel>(
            new UpdateTraineeNoteSource(
                existing,
                command.TrainerId,
                command.Data,
                DateTimeOffset.UtcNow),
            _mapper.CreateContext());
        await _notes.UpdateNoteAsync(updated, cancellationToken);

        var history = _mapper.Map<TraineeNoteHistorySource, CoachingTraineeNoteHistoryWriteModel>(
            new TraineeNoteHistorySource(
                Id<TraineeNoteHistoryEntity>.New(),
                updated.Id,
                command.TrainerId,
                DateTimeOffset.UtcNow,
                existing.Content,
                updated.Content,
                "Updated"),
            _mapper.CreateContext());
        await _notes.AddHistoryEntryAsync(history, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (updated.VisibleToTrainee || existing.VisibleToTrainee)
        {
            await _commands.EnqueueAsync(new TraineeNoteUpdatedInAppNotificationCommand
            {
                TraineeNoteId = updated.Id,
                TraineeId = updated.TraineeId,
                TrainerId = command.TrainerId,
                NoteTitle = updated.Title,
                TriggeredAt = DateTimeOffset.UtcNow
            });
        }

        var persisted = await _notes.FindNoteByIdAsync(updated.Id, cancellationToken);
        var readModel = _mapper.Map<CoachingTraineeNoteFact, TraineeNoteReadModel>(
            persisted!,
            _mapper.CreateContext());
        return Result<TraineeNoteReadModel, AppError>.Success(readModel);
    }
}
