using LgymApi.Domain.Enums;

namespace LgymApi.Application.Coaching.Invitations.PublicStatus;

public sealed record PublicInvitationStatusReadModel(TrainerInvitationStatus Status, bool UserExists);
