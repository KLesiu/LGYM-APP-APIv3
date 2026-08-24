using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleHistory;

internal sealed record GetVisibleTraineeNoteHistoryQuery(
    Id<UserEntity> TraineeId,
    Id<TraineeNoteEntity> NoteId);
