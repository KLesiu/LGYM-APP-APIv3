using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.Delete;

public sealed record DeleteTraineeManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<PlanReference> PlanId);
