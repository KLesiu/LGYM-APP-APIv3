using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.Coaching.TraineeNotes.VisibleHistory;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Coaching.ApiAdapters;

internal sealed class TrainerTraineeNotesApiAdapter : ITrainerTraineeNotesApiPort
{
    private readonly IListTrainerNotesUseCase _listNotes;
    private readonly ICreateTraineeNoteUseCase _createNote;
    private readonly IUpdateTraineeNoteUseCase _updateNote;
    private readonly IDeleteTraineeNoteUseCase _deleteNote;
    private readonly IGetTraineeNoteHistoryUseCase _getHistory;
    private readonly IMapper _mapper;

    public TrainerTraineeNotesApiAdapter(IListTrainerNotesUseCase listNotes, ICreateTraineeNoteUseCase createNote, IUpdateTraineeNoteUseCase updateNote, IDeleteTraineeNoteUseCase deleteNote, IGetTraineeNoteHistoryUseCase getHistory, IMapper mapper)
    {
        _listNotes = listNotes;
        _createNote = createNote;
        _updateNote = updateNote;
        _deleteNote = deleteNote;
        _getHistory = getHistory;
        _mapper = mapper;
    }

    public Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> GetNotesAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _listNotes.ExecuteAsync(_mapper.Map<TrainerTraineeAccountInput, ListTrainerNotesQuery>(new(trainer.Id, traineeId)), cancellationToken);

    public Task<Result<TraineeNoteReadModel, AppError>> CreateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, TraineeNoteUpsertData data, CancellationToken cancellationToken = default)
        => _createNote.ExecuteAsync(_mapper.Map<CreateNoteAccountInput, CreateTraineeNoteCommand>(new(trainer.Id, traineeId, data)), cancellationToken);

    public Task<Result<TraineeNoteReadModel, AppError>> UpdateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<TraineeNote> noteId, TraineeNoteUpsertData data, CancellationToken cancellationToken = default)
        => _updateNote.ExecuteAsync(_mapper.Map<UpdateNoteAccountInput, UpdateTraineeNoteCommand>(new(trainer.Id, traineeId, noteId, data)), cancellationToken);

    public Task<Result<Unit, AppError>> DeleteAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<TraineeNote> noteId, CancellationToken cancellationToken = default)
        => _deleteNote.ExecuteAsync(_mapper.Map<ActorTraineeNoteAccountInput, DeleteTraineeNoteCommand>(new(trainer.Id, traineeId, noteId)), cancellationToken);

    public Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> GetHistoryAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<TraineeNote> noteId, CancellationToken cancellationToken = default)
        => _getHistory.ExecuteAsync(_mapper.Map<ActorTraineeNoteAccountInput, GetTraineeNoteHistoryQuery>(new(trainer.Id, traineeId, noteId)), cancellationToken);
}

internal sealed class TraineeNotesApiAdapter : ITraineeNotesApiPort
{
    private readonly IListVisibleTraineeNotesUseCase _listNotes;
    private readonly IGetVisibleTraineeNoteUseCase _getNote;
    private readonly IGetVisibleTraineeNoteHistoryUseCase _getHistory;
    private readonly IMapper _mapper;

    public TraineeNotesApiAdapter(IListVisibleTraineeNotesUseCase listNotes, IGetVisibleTraineeNoteUseCase getNote, IGetVisibleTraineeNoteHistoryUseCase getHistory, IMapper mapper)
    {
        _listNotes = listNotes;
        _getNote = getNote;
        _getHistory = getHistory;
        _mapper = mapper;
    }

    public Task<Result<IReadOnlyList<TraineeNoteReadModel>, AppError>> GetVisibleNotesAsync(AuthenticatedAccountContext trainee, CancellationToken cancellationToken = default)
        => _listNotes.ExecuteAsync(_mapper.Map<ActorAccountInput, ListVisibleTraineeNotesQuery>(new(trainee.Id)), cancellationToken);

    public Task<Result<TraineeNoteReadModel, AppError>> GetVisibleNoteAsync(AuthenticatedAccountContext trainee, Id<TraineeNote> noteId, CancellationToken cancellationToken = default)
        => _getNote.ExecuteAsync(_mapper.Map<ActorNoteAccountInput, GetVisibleTraineeNoteQuery>(new(trainee.Id, noteId)), cancellationToken);

    public Task<Result<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>> GetVisibleHistoryAsync(AuthenticatedAccountContext trainee, Id<TraineeNote> noteId, CancellationToken cancellationToken = default)
        => _getHistory.ExecuteAsync(_mapper.Map<ActorNoteAccountInput, GetVisibleTraineeNoteHistoryQuery>(new(trainee.Id, noteId)), cancellationToken);
}
