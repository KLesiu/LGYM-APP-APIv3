using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan.Models;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.Supplementation.DeleteTraineePlan;

internal sealed class DeleteTraineeSupplementPlanUseCase : IDeleteTraineeSupplementPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTraineeSupplementPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ISupplementationPersistence plans,
        IUnitOfWork unitOfWork)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeSupplementPlanCommand command,
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

        if (command.SupplementPlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidSupplementationError(Messages.FieldRequired));
        }

        var plan = await _plans.FindTrackedPlanByIdAsync(command.SupplementPlanId, cancellationToken);
        if (plan is null || !SupplementationAccess.IsOwnedBy(plan, command.TrainerId, command.TraineeId))
        {
            return Result<Unit, AppError>.Failure(new SupplementationNotFoundError(Messages.DidntFind));
        }

        plan.IsDeleted = true;
        plan.IsActive = false;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
