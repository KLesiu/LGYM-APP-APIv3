using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlan;

internal sealed class GetCurrentDietPlanUseCase : IGetCurrentDietPlanUseCase
{
    private readonly IDietPlanPersistence _plans;
    private readonly IMapper _mapper;

    public GetCurrentDietPlanUseCase(IDietPlanPersistence plans, IMapper mapper)
    {
        _plans = plans;
        _mapper = mapper;
    }

    public async Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        GetCurrentDietPlanQuery query,
        CancellationToken cancellationToken = default)
    {
        var plan = await _plans.GetActivePlanForTraineeAsync(query.TraineeId, cancellationToken);
        return plan is null
            ? Result<DietPlanReadModel, AppError>.Failure(new NotFoundError(Messages.DidntFind))
            : Result<DietPlanReadModel, AppError>.Success(
                _mapper.Map<DietPlan, DietPlanReadModel>(plan, _mapper.CreateContext()));
    }
}
