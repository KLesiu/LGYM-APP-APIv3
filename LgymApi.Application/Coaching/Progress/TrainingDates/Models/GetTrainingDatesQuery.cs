using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.Progress.TrainingDates;

public sealed record GetTrainingDatesQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
