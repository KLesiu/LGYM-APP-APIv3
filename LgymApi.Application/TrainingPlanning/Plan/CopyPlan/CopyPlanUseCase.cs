using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Application.TrainingPlanning.Plan.CopyPlan;

internal sealed class CopyPlanUseCase : ICopyPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CopyPlanUseCase(IPlanRepository planRepository, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(planRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _planRepository = planRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PlanReadModel, AppError>> ExecuteAsync(CopyPlanCommand input, CancellationToken cancellationToken = default)
    {
        if (input.CurrentUserId.IsEmpty)
        {
            return Result<PlanReadModel, AppError>.Failure(new PlanUnauthorizedError(Messages.Unauthorized));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var plan = await _planRepository.CopyPlanByShareCodeAsync(input.ShareCode, input.CurrentUserId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result<PlanReadModel, AppError>.Success(ToReadModel(plan));
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return Result<PlanReadModel, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static PlanReadModel ToReadModel(PlanEntity plan) => new(
        plan.Id,
        plan.UserId,
        plan.Name,
        plan.IsActive,
        plan.ShareCode);
}
