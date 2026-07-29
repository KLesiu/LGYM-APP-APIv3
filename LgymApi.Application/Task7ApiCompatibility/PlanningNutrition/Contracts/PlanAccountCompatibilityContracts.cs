using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.Compatibility.Task7.Contracts;

public interface IPlanAccountCompatibilityAdapter
{
    Task<Result<Unit, AppError>> CreateAsync(PlanCreateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateAsync(PlanUpdateAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<PlanReadModel, AppError>> GetConfigAsync(PlanGetConfigAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<bool, AppError>> HasPlanAsync(PlanHasAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<List<PlanReadModel>, AppError>> GetListAsync(PlanGetListAccountQuery query, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> SetActiveAsync(PlanSetActiveAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<PlanReadModel, AppError>> CopyAsync(PlanCopyAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<string, AppError>> GenerateShareCodeAsync(PlanGenerateShareCodeAccountCommand command, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteAsync(PlanDeleteAccountCommand command, CancellationToken cancellationToken = default);
}

public sealed record PlanCreateAccountCommand(Id<AccountReference> CurrentAccountId, Id<AccountReference> RouteAccountId, string Name);
public sealed record PlanUpdateAccountCommand(Id<AccountReference> CurrentAccountId, Id<AccountReference> RouteAccountId, Id<Plan> PlanId, string Name);
public sealed record PlanGetConfigAccountQuery(Id<AccountReference> CurrentAccountId, Id<AccountReference> RouteAccountId);
public sealed record PlanHasAccountQuery(Id<AccountReference> CurrentAccountId, Id<AccountReference> RouteAccountId);
public sealed record PlanGetListAccountQuery(Id<AccountReference> CurrentAccountId, Id<AccountReference> RouteAccountId);
public sealed record PlanSetActiveAccountCommand(Id<AccountReference> CurrentAccountId, Id<AccountReference> RouteAccountId, Id<Plan> PlanId);
public sealed record PlanCopyAccountCommand(Id<AccountReference> CurrentAccountId, string ShareCode);
public sealed record PlanGenerateShareCodeAccountCommand(Id<AccountReference> CurrentAccountId, Id<Plan> PlanId);
public sealed record PlanDeleteAccountCommand(Id<AccountReference> CurrentAccountId, Id<Plan> PlanId);
