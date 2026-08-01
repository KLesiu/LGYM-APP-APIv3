using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Nutrition.Supplementation.GetTraineePlans;

internal sealed class GetTraineeSupplementPlansUseCase : IGetTraineeSupplementPlansUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;
    private readonly IMapper _mapper;

    public GetTraineeSupplementPlansUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ISupplementationPersistence plans,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _mapper = mapper;
    }

    public async Task<Result<IReadOnlyList<SupplementPlanReadModel>, AppError>> ExecuteAsync(
        GetTraineeSupplementPlansQuery query,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var accessError = SupplementationAccess.GetTrainerAccessError(
            access.IsTrainer,
            access.HasActiveRelationship,
            query.TraineeId);
        if (accessError is not null)
        {
            return Result<IReadOnlyList<SupplementPlanReadModel>, AppError>.Failure(accessError);
        }

        var plans = await _plans.ListPlansByTrainerAndTraineeAsync(
            query.TrainerId,
            query.TraineeId,
            cancellationToken);
        var readModels = _mapper.MapList<SupplementPlan, SupplementPlanReadModel>(
            plans,
            _mapper.CreateContext());
        return Result<IReadOnlyList<SupplementPlanReadModel>, AppError>.Success(readModels);
    }
}
