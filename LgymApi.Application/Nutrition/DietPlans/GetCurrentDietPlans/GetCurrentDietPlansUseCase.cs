using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Nutrition.DietPlans.GetCurrentDietPlans;

internal sealed class GetCurrentDietPlansUseCase : IGetCurrentDietPlansUseCase
{
    private readonly IDietPlanPersistence _plans;
    private readonly IMapper _mapper;

    public GetCurrentDietPlansUseCase(IDietPlanPersistence plans, IMapper mapper)
    {
        _plans = plans;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> ExecuteAsync(
        GetCurrentDietPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        var plans = await _plans.ListActivePlansForTraineeAsync(query.TraineeId, cancellationToken);
        var readModels = _mapper.MapList<DietPlan, DietPlanReadModel>(plans, _mapper.CreateContext());
        return Result<IReadOnlyList<DietPlanReadModel>, AppError>.Success(readModels);
    }
}
