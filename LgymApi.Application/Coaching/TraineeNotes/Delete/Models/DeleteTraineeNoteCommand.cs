using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.Delete;

public sealed record DeleteTraineeNoteCommand(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    Id<TraineeNoteEntity> NoteId);
