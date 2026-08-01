using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.Update;

public sealed record UpdateTraineeNoteCommand(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    Id<TraineeNoteEntity> NoteId,
    TraineeNoteUpsertData Data);
