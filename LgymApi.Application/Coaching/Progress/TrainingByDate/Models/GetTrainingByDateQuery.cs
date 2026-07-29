using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Progress.TrainingByDate;

public sealed record GetTrainingByDateQuery(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    DateTime CreatedAt);
