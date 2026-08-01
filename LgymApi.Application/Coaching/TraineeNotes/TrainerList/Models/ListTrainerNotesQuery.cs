using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.TrainerList;

public sealed record ListTrainerNotesQuery(
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);
