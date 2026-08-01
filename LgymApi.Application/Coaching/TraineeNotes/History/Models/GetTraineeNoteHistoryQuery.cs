using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.History;

public sealed record GetTraineeNoteHistoryQuery(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    Id<TraineeNoteEntity> NoteId);
