using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;

internal sealed class UpdateTraineeDietPlanUseCase : IUpdateTraineeDietPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IDietPlanPersistence _plans;
    private readonly DietPlanHistorySnapshotFactory _historyFactory;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public UpdateTraineeDietPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IDietPlanPersistence plans,
        DietPlanHistorySnapshotFactory historyFactory,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork,
        IMapper mapper)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _historyFactory = historyFactory;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        UpdateTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default)
    {
        var upsertError = DietPlanRules.GetUpsertError(command.Data);
        if (upsertError is not null)
        {
            return Result<DietPlanReadModel, AppError>.Failure(upsertError);
        }

        var normalized = DietPlanRules.Normalize(command.Data);

        if (command.TraineeId.IsEmpty)
        {
            return Result<DietPlanReadModel, AppError>.Failure(new BadRequestError(Messages.UserIdRequired));
        }

        var access = await _relationshipAccess.GetAccessDecisionAsync(
            command.TrainerId,
            command.TraineeId,
            cancellationToken);
        var accessError = DietPlanAccess.GetTrainerAccessError(access.IsTrainer, access.HasActiveRelationship);
        if (accessError is not null)
        {
            return Result<DietPlanReadModel, AppError>.Failure(accessError);
        }

        if (command.DietPlanId.IsEmpty)
        {
            return Result<DietPlanReadModel, AppError>.Failure(new BadRequestError(Messages.FieldRequired));
        }

        var plan = await _plans.FindTrackedPlanByIdAsync(command.DietPlanId, cancellationToken);
        if (plan is null || !DietPlanAccess.IsOwnedBy(plan, command.TrainerId, command.TraineeId))
        {
            return Result<DietPlanReadModel, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        var replacement = _mapper.Map<NormalizedDietPlanData, DietPlan>(normalized, _mapper.CreateContext());
        ApplyReplacement(plan, replacement);

        await _plans.AddHistoryEntryAsync(
            _historyFactory.Create(plan, command.TrainerId, "Updated"),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (plan.IsActive)
        {
            await _commands.EnqueueAsync(new DietPlanUpdatedInAppNotificationCommand
            {
                DietPlanId = plan.Id,
                TraineeId = plan.TraineeId,
                TrainerId = command.TrainerId,
                DietPlanName = plan.Name,
                TriggeredAt = DateTimeOffset.UtcNow
            });
        }

        return Result<DietPlanReadModel, AppError>.Success(
            _mapper.Map<DietPlan, DietPlanReadModel>(plan, _mapper.CreateContext()));
    }

    private static void ApplyReplacement(DietPlan plan, DietPlan replacement)
    {
        plan.Name = replacement.Name;
        plan.StartDate = replacement.StartDate;
        plan.EndDate = replacement.EndDate;
        plan.EstimatedCalories = replacement.EstimatedCalories;
        plan.ProteinGrams = replacement.ProteinGrams;
        plan.CarbsGrams = replacement.CarbsGrams;
        plan.FatGrams = replacement.FatGrams;
        plan.Notes = replacement.Notes;
        plan.IsActive = replacement.IsActive;

        plan.Meals.Clear();
        foreach (var meal in replacement.Meals)
        {
            meal.Id = Id<DietMeal>.New();
            plan.Meals.Add(meal);
        }
    }
}
