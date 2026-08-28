using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.GetOwnPlanHistory;

internal sealed class GetOwnDietPlanHistoryUseCase : IGetOwnDietPlanHistoryUseCase
{
    private readonly IDietPlanPersistence _plans;
    private readonly IMapper _mapper;

    public GetOwnDietPlanHistoryUseCase(IDietPlanPersistence plans, IMapper mapper)
    {
        _plans = plans;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>> ExecuteAsync(
        GetOwnDietPlanHistoryQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.TraineeId.IsEmpty || query.DietPlanId.IsEmpty)
        {
            return Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>.Failure(
                new BadRequestError(Messages.FieldRequired));
        }

        var plan = await _plans.GetPlanByIdAsync(query.DietPlanId, cancellationToken);
        if (plan is null || plan.TraineeId != query.TraineeId || !plan.IsActive)
        {
            return Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>.Failure(
                new NotFoundError(Messages.DidntFind));
        }

        var history = await _plans.ListPlanHistoryAsync(plan.Id, cancellationToken);
        return Result<IReadOnlyList<DietPlanHistoryReadModel>, AppError>.Success(
            _mapper.MapList<DietPlanHistory, DietPlanHistoryReadModel>(history, _mapper.CreateContext()));
    }
}
