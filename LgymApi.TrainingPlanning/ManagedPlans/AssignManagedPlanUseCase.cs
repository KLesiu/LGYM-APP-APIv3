using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.ManagedPlans;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.ManagedPlans;

internal sealed class AssignManagedPlanUseCase : IAssignManagedPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IActivePlanPointerStore _activePlanPointerStore;
    private readonly IPlanExerciseClonePort _exerciseClone;
    private readonly IAccountReadService _accountReadService;
    private readonly IUnitOfWork _unitOfWork;

    public AssignManagedPlanUseCase(
        IPlanRepository planRepository,
        IActivePlanPointerStore activePlanPointerStore,
        IPlanExerciseClonePort exerciseClone,
        IAccountReadService accountReadService,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _activePlanPointerStore = activePlanPointerStore;
        _exerciseClone = exerciseClone;
        _accountReadService = accountReadService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(
        AssignManagedPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command is null || command.TrainerId.IsEmpty || command.TraineeId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.UserIdRequired));
        }

        if (command.PlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.FieldRequired));
        }

        var plan = await _planRepository.FindByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || (plan.UserId != command.TraineeId.Rebind<LgymApi.Domain.Entities.User>() && plan.UserId != command.TrainerId.Rebind<LgymApi.Domain.Entities.User>()))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }

        if (await _accountReadService.GetByIdAsync(command.TraineeId, cancellationToken) is null)
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            Id<PlanEntity> assignedPlanId;
            if (plan.UserId == command.TrainerId.Rebind<LgymApi.Domain.Entities.User>())
            {
                await _planRepository.ClearActivePlansAsync(command.TraineeId, cancellationToken);
                var sourceExerciseIds = await _planRepository.GetPlanExerciseIdsAsync(plan.Id, cancellationToken);
                var exerciseIdMap = await _exerciseClone.StageClonesAsync(command.TraineeId, sourceExerciseIds, cancellationToken);
                var clonedPlan = await _planRepository.ClonePlanAsync(
                    plan.Id,
                    command.TraineeId.Rebind<LgymApi.Domain.Entities.User>(),
                    exerciseIdMap,
                    isActive: true,
                    cancellationToken);
                assignedPlanId = clonedPlan.Id;
            }
            else
            {
                await _planRepository.SetActivePlanAsync(command.TraineeId, command.PlanId, cancellationToken);
                assignedPlanId = command.PlanId.Rebind<PlanEntity>();
            }

            await _activePlanPointerStore.StageActivePlanIdAsync(command.TraineeId, assignedPlanId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<Unit, AppError>.Success(Unit.Value);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
