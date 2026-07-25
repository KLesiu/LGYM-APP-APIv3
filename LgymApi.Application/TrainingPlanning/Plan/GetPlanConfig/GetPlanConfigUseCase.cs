using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig;

internal sealed class GetPlanConfigUseCase : IGetPlanConfigUseCase
{
    private readonly IPlanRepository _planRepository;

    public GetPlanConfigUseCase(IPlanRepository planRepository)
    {
        ArgumentNullException.ThrowIfNull(planRepository);
        _planRepository = planRepository;
    }

    public async Task<Result<PlanReadModel, AppError>> ExecuteAsync(GetPlanConfigQuery input, CancellationToken cancellationToken = default)
    {
        if (input.CurrentUserId.IsEmpty || input.RouteUserId.IsEmpty)
        {
            return Result<PlanReadModel, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        if (input.CurrentUserId != input.RouteUserId)
        {
            return Result<PlanReadModel, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
        }

        var plan = await _planRepository.FindActiveReadModelByUserIdAsync(input.CurrentUserId, cancellationToken);
        return plan is null
            ? Result<PlanReadModel, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind))
            : Result<PlanReadModel, AppError>.Success(plan);
    }
}
