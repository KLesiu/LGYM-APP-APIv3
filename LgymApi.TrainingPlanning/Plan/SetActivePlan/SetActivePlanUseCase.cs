using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;

internal sealed class SetActivePlanUseCase : ISetActivePlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IActivePlanPointerStore _activePlanPointerStore;
    private readonly IUnitOfWork _unitOfWork;

    public SetActivePlanUseCase(
        IPlanRepository planRepository,
        IActivePlanPointerStore activePlanPointerStore,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _activePlanPointerStore = activePlanPointerStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(SetActivePlanCommand input, CancellationToken cancellationToken = default)
    {
        if (input is null || input.CurrentUserId.IsEmpty || input.RouteUserId.IsEmpty || input.PlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        if (input.CurrentUserId != input.RouteUserId)
        {
            return Result<Unit, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
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
            await _planRepository.SetActivePlanAsync(input.CurrentUserId, input.PlanId, cancellationToken);
            await _activePlanPointerStore.StageActivePlanIdAsync(input.CurrentUserId, input.PlanId.Rebind<LgymApi.Domain.Entities.Plan>(), cancellationToken);
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
