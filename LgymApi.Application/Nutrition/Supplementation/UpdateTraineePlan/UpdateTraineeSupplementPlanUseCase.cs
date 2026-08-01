using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.Supplementation.UpdateTraineePlan;

internal sealed class UpdateTraineeSupplementPlanUseCase : IUpdateTraineeSupplementPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UpdateTraineeSupplementPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        ISupplementationPersistence plans,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<SupplementPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeSupplementPlanCommand command,
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
            return Result<SupplementPlanReadModel, AppError>.Failure(accessError);
        }

        var upsertError = SupplementationRules.GetUpsertError(command.Data);
        if (upsertError is not null)
        {
            return Result<SupplementPlanReadModel, AppError>.Failure(upsertError);
        }

        if (command.PlanId.IsEmpty)
        {
            return Result<SupplementPlanReadModel, AppError>.Failure(
                new InvalidSupplementationError(Resources.Messages.FieldRequired));
        }

        var plan = await _plans.FindTrackedPlanByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || !SupplementationAccess.IsOwnedBy(plan, command.TrainerId, command.TraineeId))
        {
            return Result<SupplementPlanReadModel, AppError>.Failure(
                new SupplementationNotFoundError(Resources.Messages.DidntFind));
        }

        var replacement = _mapper.Map<NormalizedSupplementPlanData, SupplementPlan>(
            SupplementationRules.Normalize(command.Data),
            _mapper.CreateContext());
        replacement.Id = Id<SupplementPlan>.New();
        replacement.TrainerId = command.TrainerId;
        replacement.TraineeId = command.TraineeId;
        replacement.IsActive = plan.IsActive;
        replacement.IsDeleted = false;

        foreach (var item in replacement.Items)
        {
            item.Id = Id<SupplementPlanItem>.New();
            item.PlanId = replacement.Id;
        }

        plan.IsDeleted = true;
        plan.IsActive = false;
        await _plans.AddPlanAsync(replacement, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SupplementPlanReadModel, AppError>.Success(
            _mapper.Map<SupplementPlan, SupplementPlanReadModel>(replacement, _mapper.CreateContext()));
    }
}
