using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.Assign;

public sealed record AssignTraineeManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<PlanReference> PlanId);
