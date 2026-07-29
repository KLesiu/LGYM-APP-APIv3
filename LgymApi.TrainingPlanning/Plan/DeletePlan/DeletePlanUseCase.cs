using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.DeletePlan;

internal sealed class DeletePlanUseCase : IDeletePlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IPlanDayRepository _planDayRepository;
    private readonly IActivePlanPointerStore _activePlanPointerStore;
    private readonly IUnitOfWork _unitOfWork;

    public DeletePlanUseCase(
        IPlanRepository planRepository,
        IPlanDayRepository planDayRepository,
        IActivePlanPointerStore activePlanPointerStore,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _planDayRepository = planDayRepository;
        _activePlanPointerStore = activePlanPointerStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(DeletePlanCommand input, CancellationToken cancellationToken = default)
    {
        if (input is null || input.CurrentUserId.IsEmpty || input.PlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        var plan = await _planRepository.FindByIdAsync(input.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }

        if (plan.UserId != input.CurrentUserId.Rebind<LgymApi.Domain.Entities.User>())
        {
            return Result<Unit, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var activePlanId = await _activePlanPointerStore.GetActivePlanIdAsync(input.CurrentUserId, cancellationToken);
            await _planDayRepository.MarkDeletedByPlanIdAsync(plan.Id, cancellationToken);

            plan.IsActive = false;
            plan.IsDeleted = true;
            await _planRepository.UpdateAsync(plan, cancellationToken);

            if (activePlanId == plan.Id)
            {
                var lastValidPlan = await _planRepository.FindLastActiveByUserIdAsync(input.CurrentUserId, cancellationToken);
                if (lastValidPlan is not null)
                {
                    await _planRepository.SetActivePlanAsync(input.CurrentUserId, lastValidPlan.Id, cancellationToken);
                }

                await _activePlanPointerStore.StageActivePlanIdAsync(input.CurrentUserId, lastValidPlan?.Id, cancellationToken);
            }

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
