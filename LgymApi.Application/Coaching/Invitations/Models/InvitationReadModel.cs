using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations.Models;

public sealed record InvitationReadModel(
    Id<TrainerInvitationEntity> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity>? TraineeId,
    string InviteeEmail,
    string Code,
    TrainerInvitationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RespondedAt,
    DateTimeOffset CreatedAt,
    string? TraineeName,
    string? TraineeEmail);
