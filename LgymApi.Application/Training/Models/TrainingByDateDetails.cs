using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Application.Features.Training.Models;

public sealed class TrainingByDateDetails
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Training> Id { get; init; }
    public LgymApi.Domain.ValueObjects.Id<PlanDayReference> TypePlanDayId { get; init; }
    public DateTime CreatedAt { get; init; }
    public PlanDayReferenceReadModel? PlanDay { get; init; }
    public string? Gym { get; init; }
    public List<EnrichedExercise> Exercises { get; init; } = new();
}
