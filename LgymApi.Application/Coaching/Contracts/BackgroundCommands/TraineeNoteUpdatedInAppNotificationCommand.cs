using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Contracts.BackgroundCommands;

public sealed class TraineeNoteUpdatedInAppNotificationCommand : IActionCommand
{
    public Id<TraineeNote> TraineeNoteId { get; init; }
    public Id<User> TraineeId { get; init; }
    public Id<User> TrainerId { get; init; }
    public string? NoteTitle { get; init; }
    public DateTimeOffset TriggeredAt { get; init; }
}
