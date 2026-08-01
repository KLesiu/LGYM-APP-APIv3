using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Contracts.BackgroundCommands;

public sealed class TrainerInvitationCreatedInAppNotificationCommand : IActionCommand
{
    public Id<TrainerInvitation> InvitationId { get; init; }
    public Id<User> TraineeId { get; init; }
    public Id<User> TrainerId { get; init; }
}
