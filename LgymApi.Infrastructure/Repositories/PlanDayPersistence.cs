using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Mapping;
using LgymApi.Application.TrainingPlanning.PlanDay.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Infrastructure.Repositories;

internal sealed class PlanDayPersistence : IPlanDayPersistence
{
    private readonly IPlanRepository _plans;
    private readonly IPlanDayRepository _planDays;
    private readonly IPlanDayExerciseRepository _planDayExercises;
    private readonly IMapper _mapper;

    public PlanDayPersistence(
        IPlanRepository plans,
        IPlanDayRepository planDays,
        IPlanDayExerciseRepository planDayExercises,
        IMapper mapper)
    {
        _plans = plans;
        _planDays = planDays;
        _planDayExercises = planDayExercises;
        _mapper = mapper;
    }

    public async Task<PlanDayPlanPersistenceModel?> FindPlanAsync(Id<PlanReference> planId, CancellationToken cancellationToken = default)
        => (await _plans.FindByIdAsync(planId, cancellationToken)) is { } plan
            ? _mapper.Map<Plan, PlanDayPlanPersistenceModel>(plan)
            : null;

    public async Task<PlanDayPlanPersistenceModel?> FindActivePlanAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => (await _plans.FindActiveByUserIdAsync(accountId, cancellationToken)) is { } plan
            ? _mapper.Map<Plan, PlanDayPlanPersistenceModel>(plan)
            : null;

    public async Task<PlanDayPersistenceModel?> FindPlanDayAsync(Id<PlanDayReference> planDayId, CancellationToken cancellationToken = default)
        => (await _planDays.FindByIdAsync(planDayId.Rebind<PlanDay>(), cancellationToken)) is { } planDay
            ? _mapper.Map<PlanDay, PlanDayPersistenceModel>(planDay)
            : null;

    public async Task<IReadOnlyList<PlanDayPersistenceModel>> GetPlanDaysAsync(Id<PlanReference> planId, CancellationToken cancellationToken = default)
        => _mapper.MapList<PlanDay, PlanDayPersistenceModel>(
            await _planDays.GetByPlanIdAsync(planId.Rebind<Plan>(), cancellationToken));

    public async Task<IReadOnlyList<PlanDayPersistenceModel>> GetPlanDaysByIdsAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
        => _mapper.MapList<PlanDay, PlanDayPersistenceModel>(
            await _planDays.GetByIdsAsync(planDayIds.Select(planDayId => planDayId.Rebind<PlanDay>()).ToList(), cancellationToken));

    public async Task<IReadOnlyList<PlanDayExercisePersistenceModel>> GetPlanDayExercisesAsync(
        IReadOnlyCollection<Id<PlanDayReference>> planDayIds,
        CancellationToken cancellationToken = default)
        => _mapper.MapList<PlanDayExercise, PlanDayExercisePersistenceModel>(
            await _planDayExercises.GetByPlanDayIdsAsync(planDayIds.Select(id => id.Rebind<PlanDay>()).ToList(), cancellationToken));

    public async Task CreatePlanDayAsync(Id<PlanReference> planId, PlanDayWriteModel input, CancellationToken cancellationToken = default)
    {
        var planDay = new PlanDay
        {
            Id = Id<PlanDay>.New(),
            PlanId = planId.Rebind<Plan>(),
            Name = input.Name,
            IsDeleted = false
        };

        await _planDays.AddAsync(planDay, cancellationToken);
        await AddPlanDayExercisesAsync(planDay.Id, input.Exercises, cancellationToken);
    }

    public async Task UpdatePlanDayAsync(Id<PlanDayReference> planDayId, string name, CancellationToken cancellationToken = default)
    {
        var planDay = await _planDays.FindByIdAsync(planDayId.Rebind<PlanDay>(), cancellationToken);
        if (planDay is null)
        {
            return;
        }

        planDay.Name = name;
        await _planDays.UpdateAsync(planDay, cancellationToken);
    }

    public async Task ReplacePlanDayExercisesAsync(
        Id<PlanDayReference> planDayId,
        IReadOnlyList<PlanDayExerciseWriteModel> exercises,
        CancellationToken cancellationToken = default)
    {
        var persistedPlanDayId = planDayId.Rebind<PlanDay>();
        await _planDayExercises.RemoveByPlanDayIdAsync(persistedPlanDayId, cancellationToken);
        await AddPlanDayExercisesAsync(persistedPlanDayId, exercises, cancellationToken);
    }

    public Task MarkPlanDayDeletedAsync(Id<PlanDayReference> planDayId, CancellationToken cancellationToken = default)
        => _planDays.MarkDeletedAsync(planDayId.Rebind<PlanDay>(), cancellationToken);

    private async Task AddPlanDayExercisesAsync(
        Id<PlanDay> planDayId,
        IReadOnlyList<PlanDayExerciseWriteModel> exercises,
        CancellationToken cancellationToken)
    {
        var entities = exercises
            .Where(exercise => !exercise.ExerciseId.IsEmpty)
            .Select((exercise, order) =>
            {
                var entity = _mapper.Map<PlanDayExerciseWriteModel, PlanDayExercise>(exercise);
                entity.Id = Id<PlanDayExercise>.New();
                entity.PlanDayId = planDayId;
                entity.Order = order;
                return entity;
            })
            .ToList();

        if (entities.Count > 0)
        {
            await _planDayExercises.AddRangeAsync(entities, cancellationToken);
        }
    }
}
