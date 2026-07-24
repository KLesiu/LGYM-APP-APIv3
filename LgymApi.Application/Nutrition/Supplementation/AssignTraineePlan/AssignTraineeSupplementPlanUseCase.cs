using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.Supplementation.AssignTraineePlan;

internal sealed class AssignTraineeSupplementPlanUseCase : IAssignTraineeSupplementPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;
    private readonly IUnitOfWork _unitOfWork;

    public AssignTraineeSupplementPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ISupplementationPersistence plans,
        IUnitOfWork unitOfWork)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        AssignTraineeSupplementPlanCommand command,
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
            return Result<Unit, AppError>.Failure(
                new InvalidSupplementationError(Messages.FieldRequired));
        }

        var target = await _plans.FindTrackedPlanByIdAsync(
            command.SupplementPlanId,
            cancellationToken);
        if (target is null || !SupplementationAccess.IsOwnedBy(
                target,
                command.TrainerId,
                command.TraineeId))
        {
            return Result<Unit, AppError>.Failure(
                new SupplementationNotFoundError(Messages.DidntFind));
        }

        var candidates = await _plans.ListTrackedPlansByTrainerAndTraineeAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        foreach (var candidate in candidates.Where(candidate =>
                     candidate.IsActive
                     && candidate.Id != target.Id
                     && candidate.TrainerId == command.TrainerId
                     && candidate.TraineeId == command.TraineeId))
        {
            candidate.IsActive = false;
        }

        target.IsActive = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
