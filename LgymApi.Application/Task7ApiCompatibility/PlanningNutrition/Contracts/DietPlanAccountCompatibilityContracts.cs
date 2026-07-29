using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Compatibility.Task7.Contracts;

public interface IDietPlanAccountCompatibilityAdapter
{
    Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> GetTraineePlansAsync(DietPlanListAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<DietPlanReadModel, AppError>> GetTraineePlanAsync(DietPlanGetAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<DietPlanReadModel, AppError>> CreateAsync(DietPlanCreateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<DietPlanReadModel, AppError>> UpdateAsync(DietPlanUpdateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> ActivateAsync(DietPlanActivateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(DietPlanDeleteAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>> GetHistoryAsync(DietPlanHistoryAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> GetCurrentPlansAsync(DietPlanCurrentAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<DietPlanReadModel, AppError>> GetCurrentPlanAsync(DietPlanCurrentAccountQuery query, CancellationToken cancellationToken = default);
}

public sealed record DietPlanListAccountQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId);
public sealed record DietPlanGetAccountQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<DietPlan> DietPlanId);
public sealed record DietPlanCreateAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, DietPlanUpsertData Data);
public sealed record DietPlanUpdateAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<DietPlan> DietPlanId, DietPlanUpsertData Data);
public sealed record DietPlanActivateAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<DietPlan> DietPlanId);
public sealed record DietPlanDeleteAccountCommand(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<DietPlan> DietPlanId);
public sealed record DietPlanHistoryAccountQuery(Id<AccountReference> TrainerId, Id<AccountReference> TraineeId, Id<DietPlan> DietPlanId);
public sealed record DietPlanCurrentAccountQuery(Id<AccountReference> TraineeId);
