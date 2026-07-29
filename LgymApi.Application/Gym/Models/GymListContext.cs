using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Features.Gym.Models;

public sealed class GymListContext
{
    public List<WorkoutGymPersistenceModel> Gyms { get; init; } = new();
    public Dictionary<Id<LgymApi.Domain.Entities.Gym>, WorkoutTrainingPersistenceModel> LastTrainings { get; init; } = new();
    public Dictionary<Id<PlanDayReference>, PlanDayReferenceReadModel> PlanDays { get; init; } = new();
}
