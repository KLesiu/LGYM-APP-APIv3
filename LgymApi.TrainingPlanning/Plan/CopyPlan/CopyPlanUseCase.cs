using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Resources;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.Plan.CopyPlan;

internal sealed class CopyPlanUseCase : ICopyPlanUseCase
{
    private readonly IPlanRepository _planRepository;
    private readonly IPlanExerciseClonePort _exerciseClone;
    private readonly IUnitOfWork _unitOfWork;

    public CopyPlanUseCase(
        IPlanRepository planRepository,
        IPlanExerciseClonePort exerciseClone,
        IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(planRepository);
        ArgumentNullException.ThrowIfNull(exerciseClone);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _planRepository = planRepository;
        _exerciseClone = exerciseClone;
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
            var sourcePlan = await _planRepository.FindByShareCodeAsync(input.ShareCode, cancellationToken);
            if (sourcePlan is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Result<PlanReadModel, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind));
            }

            var sourceExerciseIds = await _planRepository.GetPlanExerciseIdsAsync(sourcePlan.Id, cancellationToken);
            var exerciseIdMap = await _exerciseClone.StageClonesAsync(input.CurrentUserId, sourceExerciseIds, cancellationToken);
            var plan = await _planRepository.ClonePlanAsync(
                sourcePlan.Id.Rebind<PlanReference>(),
                input.CurrentUserId,
                exerciseIdMap,
                isActive: true,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result<PlanReadModel, AppError>.Success(ToReadModel(plan));
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
