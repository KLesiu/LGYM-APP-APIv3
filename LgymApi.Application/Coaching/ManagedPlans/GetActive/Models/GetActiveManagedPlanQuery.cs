using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.GetActive;

public sealed record GetActiveManagedPlanQuery(Id<AccountReference> TraineeId);
