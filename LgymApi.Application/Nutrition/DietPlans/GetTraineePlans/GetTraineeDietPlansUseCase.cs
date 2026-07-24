using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;

internal sealed class GetTraineeDietPlansUseCase : IGetTraineeDietPlansUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IDietPlanPersistence _plans;
    private readonly IMapper _mapper;

    public GetTraineeDietPlansUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IDietPlanPersistence plans,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<DietPlanReadModel>, AppError>> ExecuteAsync(
        GetTraineeDietPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.TraineeId.IsEmpty)
        {
            return Result<IReadOnlyList<DietPlanReadModel>, AppError>.Failure(new BadRequestError(Messages.UserIdRequired));
        }

        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = DietPlanAccess.GetTrainerAccessError(access.IsTrainer, access.HasActiveRelationship);
        if (accessError is not null)
        {
            return Result<IReadOnlyList<DietPlanReadModel>, AppError>.Failure(accessError);
        }

        var plans = await _plans.ListPlansByTrainerAndTraineeAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var readModels = _mapper.MapList<DietPlan, DietPlanReadModel>(plans, _mapper.CreateContext());
        return Result<IReadOnlyList<DietPlanReadModel>, AppError>.Success(readModels);
    }
}
