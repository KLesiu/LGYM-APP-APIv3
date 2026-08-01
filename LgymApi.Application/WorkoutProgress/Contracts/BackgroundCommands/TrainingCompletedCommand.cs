using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;

public sealed class TrainingCompletedCommand : IActionCommand
{
    public Id<User> UserId { get; init; }

    public Id<Training> TrainingId { get; init; }
}
