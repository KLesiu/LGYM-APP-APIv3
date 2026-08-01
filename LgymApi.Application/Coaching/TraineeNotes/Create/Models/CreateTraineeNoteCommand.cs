using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.Create;

public sealed record CreateTraineeNoteCommand(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    TraineeNoteUpsertData Data);
