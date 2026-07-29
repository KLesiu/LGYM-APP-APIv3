using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.ManagedPlans.List;

public sealed record ListManagedPlansQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
