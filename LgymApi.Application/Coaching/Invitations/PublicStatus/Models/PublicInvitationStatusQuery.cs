using LgymApi.Domain.ValueObjects;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;

namespace LgymApi.Application.Coaching.Invitations.PublicStatus;

public sealed record PublicInvitationStatusQuery(Id<TrainerInvitationEntity> InvitationId, string Code);
