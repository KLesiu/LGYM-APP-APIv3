using LgymApi.Application.Coaching.Contracts.Access;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Common.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;

internal sealed class CreateTraineeDietPlanUseCase : ICreateTraineeDietPlanUseCase
{
    private readonly ICoachingRelationshipAccessService _relationshipAccess;
    private readonly IDietPlanPersistence _plans;
    private readonly ICommandDispatcher _commands;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;
    private readonly DietPlanHistorySnapshotFactory _historyFactory;

    public CreateTraineeDietPlanUseCase(
        ICoachingRelationshipAccessService relationshipAccess,
        IDietPlanPersistence plans,
        ICommandDispatcher commands,
        IUnitOfWork unitOfWork,
        IMapper mapper,
        DietPlanHistorySnapshotFactory historyFactory)
    {
        _relationshipAccess = relationshipAccess;
        _plans = plans;
        _commands = commands;
        _unitOfWork = unitOfWork;
        _mapper = mapper;
        _historyFactory = historyFactory;
    }

    public async Task<Result<DietPlanReadModel, AppError>> ExecuteAsync(
        CreateTraineeDietPlanCommand command,
        CancellationToken cancellationToken = default)
    {
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

        var upsertError = DietPlanRules.GetUpsertError(command.Data);
        if (upsertError is not null)
        {
            return Result<DietPlanReadModel, AppError>.Failure(upsertError);
        }

        var plan = _mapper.Map<NormalizedDietPlanData, DietPlan>(
            DietPlanRules.Normalize(command.Data),
            _mapper.CreateContext());
        plan.Id = Id<DietPlan>.New();
        plan.TrainerId = command.TrainerId;
        plan.TraineeId = command.TraineeId;
        plan.IsDeleted = false;

        foreach (var meal in plan.Meals)
        {
            meal.Id = Id<DietMeal>.New();
            meal.DietPlanId = plan.Id;
        }

        await _plans.AddPlanAsync(plan, cancellationToken);
        await _plans.AddHistoryEntryAsync(
            _historyFactory.Create(plan, command.TrainerId, "Created"),
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
}
