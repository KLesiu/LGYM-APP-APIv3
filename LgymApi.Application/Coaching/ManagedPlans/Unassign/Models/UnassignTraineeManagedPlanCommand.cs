using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.Unassign;

public sealed record UnassignTraineeManagedPlanCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
