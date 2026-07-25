using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PlanDay.Models;
using LgymApi.Domain.ValueObjects;
using PlanDayEntity = LgymApi.Domain.Entities.PlanDay;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.PlanDay;

public interface IPlanDayService
{
    Task<Result<Unit, AppError>> CreatePlanDayAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Plan> planId, string name, IReadOnlyCollection<PlanDayExerciseInput> exercises, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdatePlanDayAsync(UserEntity currentUser, Id<PlanDayEntity> planDayId, string name, IReadOnlyCollection<PlanDayExerciseInput> exercises, CancellationToken cancellationToken = default);
    Task<Result<PlanDayDetailsContext, AppError>> GetPlanDayAsync(UserEntity currentUser, Id<PlanDayEntity> planDayId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<PlanDaysContext, AppError>> GetPlanDaysAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Plan> planId, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default);
    Task<Result<List<PlanDayEntity>, AppError>> GetPlanDaysTypesAsync(UserEntity currentUser, Id<UserEntity> routeUserId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeletePlanDayAsync(UserEntity currentUser, Id<PlanDayEntity> planDayId, CancellationToken cancellationToken = default);
    Task<Result<PlanDaysInfoContext, AppError>> GetPlanDaysInfoAsync(UserEntity currentUser, Id<LgymApi.Domain.Entities.Plan> planId, CancellationToken cancellationToken = default);
}
