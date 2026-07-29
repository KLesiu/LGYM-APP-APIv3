using LgymApi.Application.Features.Training.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.WorkoutProgress.TrainingExecution;

public sealed record CompleteTrainingInput(
    Id<Gym> GymId,
    Id<PlanDayReference> PlanDayId,
    DateTime CreatedAt,
    IReadOnlyCollection<TrainingExerciseInput> Exercises);
