using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.GetPlansList;

internal sealed class GetPlansListUseCase : IGetPlansListUseCase
{
    private readonly IPlanRepository _planRepository;

    public GetPlansListUseCase(IPlanRepository planRepository)
    {
        ArgumentNullException.ThrowIfNull(planRepository);
        _planRepository = planRepository;
    }

    public async Task<Result<List<PlanReadModel>, AppError>> ExecuteAsync(GetPlansListQuery input, CancellationToken cancellationToken = default)
    {
        if (input.CurrentUserId.IsEmpty || input.RouteUserId.IsEmpty)
        {
            return Result<List<PlanReadModel>, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        if (input.CurrentUserId != input.RouteUserId)
        {
            return Result<List<PlanReadModel>, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
        }

        var plans = await _planRepository.GetReadModelsByUserIdAsync(input.CurrentUserId, cancellationToken);
        return plans.Count == 0
            ? Result<List<PlanReadModel>, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind))
            : Result<List<PlanReadModel>, AppError>.Success(plans);
    }
}
