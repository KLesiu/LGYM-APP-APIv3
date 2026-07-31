using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Coaching.ApiAdapters;

public interface IManagedPlanAccountApiAdapter
{
    Task<Result<IReadOnlyList<ManagedPlanReadModel>, AppError>> ListAsync(ManagedPlanListAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<ManagedPlanReadModel, AppError>> CreateAsync(ManagedPlanCreateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<ManagedPlanReadModel, AppError>> UpdateAsync(ManagedPlanUpdateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(ManagedPlanDeleteAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> AssignAsync(ManagedPlanAssignAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UnassignAsync(ManagedPlanUnassignAccountCommand command, CancellationToken cancellationToken = default);
}

public sealed record ManagedPlanListAccountQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
public sealed record ManagedPlanCreateAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, string Name);
public sealed record ManagedPlanUpdateAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<Plan> PlanId, string Name);
public sealed record ManagedPlanDeleteAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<Plan> PlanId);
public sealed record ManagedPlanAssignAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<Plan> PlanId);
public sealed record ManagedPlanUnassignAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
