using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan;

internal sealed class DeleteTraineeDietPlanUseCase : IDeleteTraineeDietPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IDietPlanPersistence _plans;
    private readonly DietPlanHistorySnapshotFactory _historyFactory;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteTraineeDietPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IDietPlanPersistence plans,
        DietPlanHistorySnapshotFactory historyFactory,
        IUnitOfWork unitOfWork)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _historyFactory = historyFactory;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        DeleteTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.TraineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new BadRequestError(Messages.UserIdRequired));
        }

        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = DietPlanAccess.GetTrainerAccessError(access.IsTrainer, access.HasActiveRelationship);
        if (accessError is not null)
        {
            return Result<Unit, AppError>.Failure(accessError);
        }

        if (command.DietPlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new BadRequestError(Messages.FieldRequired));
        }

        var plan = await _plans.FindTrackedPlanByIdAsync(command.DietPlanId, cancellationToken);
        if (plan is null || !DietPlanAccess.IsOwnedBy(plan, command.TrainerId, command.TraineeId))
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        plan.IsDeleted = true;
        plan.IsActive = false;
        await _plans.AddHistoryEntryAsync(
            _historyFactory.Create(plan, command.TrainerId, "Deleted"),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
