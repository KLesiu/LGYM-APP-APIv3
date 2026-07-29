using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;

namespace LgymApi.Application.Features.Tutorial.Models;

public sealed class TutorialProgressResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.UserTutorialProgress> Id { get; init; }
    public TutorialType TutorialType { get; init; }
    public string TutorialName { get; init; } = string.Empty;
    public string TutorialDescription { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public DateTime? CompletedAt { get; init; }
    public List<TutorialStep> CompletedSteps { get; init; } = new();
    public List<TutorialStep> RemainingSteps { get; init; } = new();
    public int TotalSteps { get; init; }
    public int CompletedStepsCount { get; init; }
}
