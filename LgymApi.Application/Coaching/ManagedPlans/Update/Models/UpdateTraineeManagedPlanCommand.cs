using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.Update;

public sealed record UpdateTraineeManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    Id<PlanReference> PlanId,
    string Name);
