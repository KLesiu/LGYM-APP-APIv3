using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Nutrition.Supplementation.CreateTraineePlan;

internal sealed class CreateTraineeSupplementPlanUseCase : ICreateTraineeSupplementPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly ISupplementationPersistence _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public CreateTraineeSupplementPlanUseCase(
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
        CreateTraineeSupplementPlanCommand command,
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

        var plan = _mapper.Map<NormalizedSupplementPlanData, SupplementPlan>(
            SupplementationRules.Normalize(command.Data),
            _mapper.CreateContext());
        plan.Id = Id<SupplementPlan>.New();
        plan.TrainerId = command.TrainerId;
        plan.TraineeId = command.TraineeId;
        plan.IsActive = false;
        plan.IsDeleted = false;

        foreach (var item in plan.Items)
        {
            item.Id = Id<SupplementPlanItem>.New();
            item.PlanId = plan.Id;
        }

        await _plans.AddPlanAsync(plan, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<SupplementPlanReadModel, AppError>.Success(
            _mapper.Map<SupplementPlan, SupplementPlanReadModel>(plan, _mapper.CreateContext()));
    }
}
