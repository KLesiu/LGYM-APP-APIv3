using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Features.Training.Models;

public sealed record AddTrainingInput(
    Id<LgymApi.Domain.Entities.Gym> GymId,
    Id<PlanDayReference> PlanDayId,
    DateTime CreatedAt,
    IReadOnlyCollection<TrainingExerciseInput> Exercises);
