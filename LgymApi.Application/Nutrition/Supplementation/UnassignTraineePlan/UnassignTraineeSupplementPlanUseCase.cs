using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan.Contracts;
using LgymApi.Application.Repositories;

namespace LgymApi.Application.Nutrition.Supplementation.UnassignTraineePlan;

internal sealed class UnassignTraineeSupplementPlanUseCase : IUnassignTraineeSupplementPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;
    private readonly IUnitOfWork _unitOfWork;

    public UnassignTraineeSupplementPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ISupplementationPersistence plans,
        IUnitOfWork unitOfWork)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        UnassignTraineeSupplementPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = SupplementationAccess.GetTrainerAccessError(
            access.IsTrainer,
            access.HasActiveRelationship,
            command.TraineeId);
        if (accessError is not null)
        {
            return Result<Unit, AppError>.Failure(accessError);
        }

        var activePlan = await _plans.GetTrackedActivePlanForTraineeAsync(
            command.TraineeId,
            cancellationToken);
        if (activePlan is null || activePlan.TrainerId != command.TrainerId)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        activePlan.IsActive = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
