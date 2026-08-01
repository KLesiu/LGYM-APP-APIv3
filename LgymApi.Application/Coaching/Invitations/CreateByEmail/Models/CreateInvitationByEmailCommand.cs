using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.CreateByEmail;

public sealed record CreateInvitationByEmailCommand(
    Id<UserEntity> TrainerId,
    string InviteeEmail,
    string PreferredLanguage,
    string PreferredTimeZone);
