using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.TrainingPlanning.PlanDay.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.TrainingPlanning.PlanDay;

internal sealed class PlanDayService : IPlanDayService
{
    private readonly IPlanDayPersistence _persistence;
    private readonly IPlanDayRelationshipAccessPort _relationshipAccess;
    private readonly IPlanExerciseCatalogPort _exerciseCatalog;
    private readonly IPlanTrainingActivityPort _trainingActivity;
    private readonly IUnitOfWork _unitOfWork;

    public PlanDayService(
        IPlanDayPersistence persistence,
        IPlanDayRelationshipAccessPort relationshipAccess,
        IPlanExerciseCatalogPort exerciseCatalog,
        IPlanTrainingActivityPort trainingActivity,
        IUnitOfWork unitOfWork)
    {
        _persistence = persistence;
        _relationshipAccess = relationshipAccess;
        _exerciseCatalog = exerciseCatalog;
        _trainingActivity = trainingActivity;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> CreateAsync(CreatePlanDayCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.CurrentAccountId.IsEmpty || command.PlanId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var plan = await _persistence.FindPlanAsync(command.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(command.CurrentAccountId, plan.OwnerId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        if (string.IsNullOrWhiteSpace(command.Input.Name) || command.Input.Exercises.Count == 0)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.FieldRequired));
        }

        await _persistence.CreatePlanDayAsync(command.PlanId, command.Input, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UpdateAsync(UpdatePlanDayCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.CurrentAccountId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (string.IsNullOrWhiteSpace(command.Input.Name) || command.Input.Exercises.Count == 0)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.FieldRequired));
        }

        if (command.PlanDayId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.DidntFind));
        }

        var planDay = await _persistence.FindPlanDayAsync(command.PlanDayId, cancellationToken);
        if (planDay is null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var plan = await _persistence.FindPlanAsync(planDay.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(command.CurrentAccountId, plan.OwnerId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _persistence.UpdatePlanDayAsync(command.PlanDayId, command.Input.Name, cancellationToken);
            await _persistence.ReplacePlanDayExercisesAsync(command.PlanDayId, command.Input.Exercises, cancellationToken);
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

    public async Task<Result<PlanDayReadModel, AppError>> GetAsync(GetPlanDayQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || query.CurrentAccountId.IsEmpty || query.PlanDayId.IsEmpty)
        {
            return Result<PlanDayReadModel, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var planDay = await _persistence.FindPlanDayAsync(query.PlanDayId, cancellationToken);
        if (planDay is null)
        {
            return Result<PlanDayReadModel, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var plan = await _persistence.FindPlanAsync(planDay.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<PlanDayReadModel, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(query.CurrentAccountId, plan.OwnerId, cancellationToken))
        {
            return Result<PlanDayReadModel, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var exercises = await _persistence.GetPlanDayExercisesAsync([planDay.Id], cancellationToken);
        return await BuildReadModelAsync(planDay, exercises, query.Cultures, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<PlanDayReadModel>, AppError>> GetForPlanAsync(GetPlanDaysQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || query.CurrentAccountId.IsEmpty || query.PlanId.IsEmpty)
        {
            return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var plan = await _persistence.FindPlanAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(query.CurrentAccountId, plan.OwnerId, cancellationToken))
        {
            return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var planDays = await _persistence.GetPlanDaysAsync(plan.Id, cancellationToken);
        if (planDays.Count == 0)
        {
            return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var exercises = await _persistence.GetPlanDayExercisesAsync(planDays.Select(planDay => planDay.Id).ToArray(), cancellationToken);
        var catalogResult = await GetCatalogAsync(
            exercises.Select(exercise => exercise.ExerciseId).Distinct().ToArray(),
            query.Cultures,
            cancellationToken);
        if (catalogResult.IsFailure)
        {
            return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Failure(catalogResult.Error);
        }

        var models = new List<PlanDayReadModel>(planDays.Count);
        foreach (var planDay in planDays)
        {
            models.Add(BuildReadModel(planDay, exercises, catalogResult.Value));
        }

        return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Success(models);
    }

    public async Task<Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>> GetTypesAsync(GetPlanDayTypesQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || query.CurrentAccountId.IsEmpty || query.RouteAccountId.IsEmpty)
        {
            return Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        if (query.CurrentAccountId != query.RouteAccountId)
        {
            return Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var plan = await _persistence.FindActivePlanAsync(query.CurrentAccountId, cancellationToken);
        if (plan is null)
        {
            return Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var planDays = await _persistence.GetPlanDaysAsync(plan.Id, cancellationToken);
        return Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>.Success(
            planDays.Select(planDay => new PlanDayChoiceReadModel(planDay.Id, planDay.Name)).ToArray());
    }

    public async Task<Result<Unit, AppError>> DeleteAsync(DeletePlanDayCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.CurrentAccountId.IsEmpty || command.PlanDayId.IsEmpty)
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var planDay = await _persistence.FindPlanDayAsync(command.PlanDayId, cancellationToken);
        if (planDay is null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        var plan = await _persistence.FindPlanAsync(planDay.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(command.CurrentAccountId, plan.OwnerId, cancellationToken))
        {
            return Result<Unit, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        await _persistence.MarkPlanDayDeletedAsync(command.PlanDayId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>> GetInfoAsync(GetPlanDaysInfoQuery query, CancellationToken cancellationToken = default)
    {
        if (query is null || query.CurrentAccountId.IsEmpty || query.PlanId.IsEmpty)
        {
            return Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>.Failure(new InvalidPlanDayError(Messages.InvalidId));
        }

        var plan = await _persistence.FindPlanAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind));
        }

        if (!await CanAccessPlanAsync(query.CurrentAccountId, plan.OwnerId, cancellationToken))
        {
            return Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>.Failure(new PlanDayForbiddenError(Messages.Forbidden));
        }

        var planDays = await _persistence.GetPlanDaysAsync(plan.Id, cancellationToken);
        var planDayIds = planDays.Select(planDay => planDay.Id).ToArray();
        var exercises = await _persistence.GetPlanDayExercisesAsync(planDayIds, cancellationToken);
        var lastTrainingDates = await _trainingActivity.GetLastTrainingDatesAsync(planDayIds, cancellationToken);

        var models = planDays.Select(planDay =>
        {
            var dayExercises = exercises.Where(exercise => exercise.PlanDayId == planDay.Id).ToArray();
            var lastTrainingDate = lastTrainingDates.TryGetValue(planDay.Id, out var date) ? date : null;
            return new PlanDayInfoReadModel(
                planDay.Id,
                planDay.Name,
                lastTrainingDate,
                dayExercises.Sum(exercise => exercise.Series),
                dayExercises.Length);
        }).ToArray();

        return Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>.Success(models);
    }

    private async Task<Result<PlanDayReadModel, AppError>> BuildReadModelAsync(
        PlanDayPersistenceModel planDay,
        IReadOnlyList<PlanDayExercisePersistenceModel> allExercises,
        IReadOnlyList<string> cultures,
        CancellationToken cancellationToken)
    {
        var exercises = allExercises.Where(exercise => exercise.PlanDayId == planDay.Id).ToArray();
        var exerciseIds = exercises.Select(exercise => exercise.ExerciseId).Distinct().ToArray();
        var catalogResult = await GetCatalogAsync(exerciseIds, cultures, cancellationToken);
        return catalogResult.IsFailure
            ? Result<PlanDayReadModel, AppError>.Failure(catalogResult.Error)
            : Result<PlanDayReadModel, AppError>.Success(BuildReadModel(planDay, allExercises, catalogResult.Value));
    }

    private static PlanDayReadModel BuildReadModel(
        PlanDayPersistenceModel planDay,
        IReadOnlyList<PlanDayExercisePersistenceModel> allExercises,
        IReadOnlyDictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem> definitionById)
    {
        var exercises = allExercises.Where(exercise => exercise.PlanDayId == planDay.Id).ToArray();

        return new PlanDayReadModel(
            planDay.Id,
            planDay.Name,
            exercises.Select(exercise => new PlanDayExerciseReadModel(
                exercise.ExerciseId,
                exercise.Order,
                exercise.Series,
                exercise.Reps,
                definitionById.TryGetValue(exercise.ExerciseId, out var definition)
                    ? new PlanExerciseReadModel(
                        definition.Id,
                        definition.Name,
                        definition.OwnerId,
                        definition.BodyPart,
                        definition.EloFormula,
                        definition.Description,
                        definition.Image)
                    : null)).ToArray());
    }

    private async Task<Result<IReadOnlyDictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem>, AppError>> GetCatalogAsync(
        IReadOnlyCollection<Id<PlanExerciseReference>> exerciseIds,
        IReadOnlyList<string> cultures,
        CancellationToken cancellationToken)
    {
        var catalog = await _exerciseCatalog.GetByIdsAsync(exerciseIds, cultures, cancellationToken);
        return exerciseIds.All(catalog.ContainsKey)
            ? Result<IReadOnlyDictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem>, AppError>.Success(catalog)
            : Result<IReadOnlyDictionary<Id<PlanExerciseReference>, PlanExerciseCatalogItem>, AppError>.Failure(
                new PlanDayNotFoundError(Messages.DidntFind));
    }

    private async Task<bool> CanAccessPlanAsync(
        Id<AccountReference> currentAccountId,
        Id<AccountReference> planOwnerId,
        CancellationToken cancellationToken)
    {
        if (currentAccountId == planOwnerId)
        {
            return true;
        }

        return await _relationshipAccess.HasActiveRelationshipAsync(currentAccountId, planOwnerId, cancellationToken);
    }
}
