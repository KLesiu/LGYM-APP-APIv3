using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.VisibleList;

public sealed record ListVisibleTraineeNotesQuery(Id<UserEntity> TraineeId);
