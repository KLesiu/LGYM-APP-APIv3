using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.Create;

public sealed record CreateTraineeManagedPlanCommand(
    Id<AccountReference> TrainerId,
    Id<AccountReference> TraineeId,
    string Name);
