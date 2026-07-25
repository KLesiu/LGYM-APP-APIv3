using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan;

internal sealed class ActivateTraineeDietPlanUseCase : IActivateTraineeDietPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IDietPlanPersistence _dietPlanPersistence;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DietPlanHistorySnapshotFactory _historySnapshotFactory;

    public ActivateTraineeDietPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IDietPlanPersistence dietPlanPersistence,
        ICommandDispatcher commandDispatcher,
        IUnitOfWork unitOfWork,
        DietPlanHistorySnapshotFactory historySnapshotFactory)
    {
        _relationshipAccess = relationshipAccess;
        _dietPlanPersistence = dietPlanPersistence;
        _commandDispatcher = commandDispatcher;
        _unitOfWork = unitOfWork;
        _historySnapshotFactory = historySnapshotFactory;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        ActivateTraineeDietPlanCommand command,
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

        var plan = await _dietPlanPersistence.FindTrackedPlanByIdAsync(command.DietPlanId, cancellationToken);
        if (plan is null || !DietPlanAccess.IsOwnedBy(plan, command.TrainerId, command.TraineeId))
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        plan.IsActive = true;
        await _dietPlanPersistence.AddHistoryEntryAsync(
            _historySnapshotFactory.Create(plan, command.TrainerId, "Activated"),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _commandDispatcher.EnqueueAsync(new DietPlanUpdatedInAppNotificationCommand
        {
            DietPlanId = plan.Id,
            TraineeId = plan.TraineeId,
            TrainerId = command.TrainerId,
            DietPlanName = plan.Name,
            TriggeredAt = DateTimeOffset.UtcNow
        });

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
