using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Coaching.Contracts.BackgroundCommands;

public sealed class InvitationRevokedCommand : IActionCommand
{
    public Id<TrainerInvitation> InvitationId { get; init; }
}
