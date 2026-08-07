using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Resources;

namespace LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;

internal sealed class UpdatePlanUseCase : IUpdatePlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePlanUseCase(IPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ExecuteAsync(UpdatePlanCommand input, CancellationToken cancellationToken = default)
    {
        if (input is null || input.CurrentUserId.IsEmpty || input.RouteUserId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        if (input.CurrentUserId != input.RouteUserId)
        {
            return Result<Unit, AppError>.Failure(new PlanForbiddenError(Messages.Forbidden));
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.FieldRequired));
        }

        if (input.PlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanError(Messages.InvalidId));
        }

        var plan = await _planRepository.FindByIdAndUserIdAsync(input.PlanId, input.CurrentUserId, cancellationToken);
        if (plan is null)
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }

        plan.Name = input.Name;
        await _planRepository.UpdateAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
