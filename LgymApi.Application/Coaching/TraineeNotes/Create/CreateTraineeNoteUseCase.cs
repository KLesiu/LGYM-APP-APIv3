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
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;

namespace LgymApi.Application.Coaching.TraineeNotes.Create;

internal sealed class CreateTraineeNoteUseCase : ICreateTraineeNoteUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ICoachingTraineeNotePersistence _notes;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public CreateTraineeNoteUseCase(
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
        CreateTraineeNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = TraineeNoteRules.GetAccessError(access, command.TraineeId);
        if (accessError is not null)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(accessError);
        }

        var upsertError = TraineeNoteRules.GetUpsertError(command.Data);
        if (upsertError is not null)
        {
            return Result<TraineeNoteReadModel, AppError>.Failure(upsertError);
        }

        var note = _mapper.Map<CreateTraineeNoteSource, CoachingTraineeNoteWriteModel>(
            new CreateTraineeNoteSource(
                Id<TraineeNoteEntity>.New(),
                command.TrainerId,
                command.TraineeId,
                command.Data,
                DateTimeOffset.UtcNow),
            _mapper.CreateContext());
        await _notes.AddNoteAsync(note, cancellationToken);

        var history = _mapper.Map<TraineeNoteHistorySource, CoachingTraineeNoteHistoryWriteModel>(
            new TraineeNoteHistorySource(
                Id<TraineeNoteHistoryEntity>.New(),
                note.Id,
                command.TrainerId,
                DateTimeOffset.UtcNow,
                null,
                note.Content,
                "Created",
                null,
                note.VisibleToTrainee),
            _mapper.CreateContext());
        await _notes.AddHistoryEntryAsync(history, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (note.VisibleToTrainee)
        {
            await _commands.EnqueueAsync(new TraineeNoteUpdatedInAppNotificationCommand
            {
                TraineeNoteId = note.Id,
                TraineeId = note.TraineeId,
                TrainerId = command.TrainerId,
                NoteTitle = note.Title,
                TriggeredAt = DateTimeOffset.UtcNow
            });
        }

        var persisted = await _notes.FindNoteByIdAsync(note.Id, cancellationToken);
        var readModel = _mapper.Map<CoachingTraineeNoteFact, TraineeNoteReadModel>(
            persisted!,
            _mapper.CreateContext());
        return Result<TraineeNoteReadModel, AppError>.Success(readModel);
    }
}
