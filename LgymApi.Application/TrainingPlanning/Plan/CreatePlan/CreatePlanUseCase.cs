using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.ActivePlanPointer;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.Plan.CreatePlan;

internal sealed class CreatePlanUseCase : ICreatePlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IActivePlanPointerStore _activePlanPointerStore;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePlanUseCase(
        IPlanRepository planRepository,
        IActivePlanPointerStore activePlanPointerStore,
        IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _activePlanPointerStore = activePlanPointerStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(CreatePlanCommand input, CancellationToken cancellationToken = default)
    {
        if (input is null || input.CurrentUserId.IsEmpty || input.RouteUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        if (input.CurrentUserId != input.RouteUserId)
        {
            return Result<Unit, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
        }

        var plan = new PlanEntity
        {
            Id = Id<PlanEntity>.New(),
            UserId = input.CurrentUserId,
            Name = input.Name,
            IsActive = true,
            IsDeleted = false
        };

        await _planRepository.AddAsync(plan, cancellationToken);
        await _activePlanPointerStore.StageActivePlanIdAsync(input.CurrentUserId, plan.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
