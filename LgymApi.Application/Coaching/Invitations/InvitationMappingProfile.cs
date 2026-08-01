using LgymApi.Application.Coaching.Invitations.Models;
using LgymApi.Application.Coaching.Invitations.PublicStatus;
using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Invitations;

public sealed class InvitationMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<CoachingInvitationFact, InvitationReadModel>((source, _) => new InvitationReadModel(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.InviteeEmail,
            source.Code,
            source.Status,
            source.ExpiresAt,
            source.RespondedAt,
            source.CreatedAt,
            null,
            null));
        configuration.CreateMap<CoachingInvitationWriteModel, InvitationReadModel>((source, _) => new InvitationReadModel(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.InviteeEmail,
            source.Code,
            source.Status,
            source.ExpiresAt,
            source.RespondedAt,
            source.CreatedAt,
            null,
            null));
        configuration.CreateMap<InvitationCreationSource, CoachingInvitationWriteModel>((source, _) => new CoachingInvitationWriteModel(
            source.Id,
            source.TrainerId,
            source.InviteeEmail,
            source.TraineeId,
            source.Code,
            TrainerInvitationStatus.Pending,
            source.ExpiresAt,
            source.CreatedAt,
            null));
        configuration.CreateMap<InvitationResponseSource, CoachingInvitationResponseUpdateModel>((source, _) => new CoachingInvitationResponseUpdateModel(
            source.Id,
            source.TraineeId,
            source.Status,
            source.RespondedAt));
        configuration.CreateMap<InvitationActiveLinkSource, CoachingActiveLinkWriteModel>((source, _) => new CoachingActiveLinkWriteModel(
            source.Id,
            source.TrainerId,
            source.TraineeId));
        configuration.CreateMap<InvitationListSource, InvitationReadModel>((source, _) => new InvitationReadModel(
            source.Invitation.Id,
            source.Invitation.TrainerId,
            source.Invitation.TraineeId,
            source.Invitation.InviteeEmail,
            source.Invitation.Code,
            source.Status,
            source.Invitation.ExpiresAt,
            source.RespondedAt,
            source.Invitation.CreatedAt,
            null,
            null));
        configuration.CreateMap<InvitationWithAccountSource, InvitationReadModel>((source, _) => new InvitationReadModel(
            source.Invitation.Id,
            source.Invitation.TrainerId,
            source.Invitation.TraineeId,
            source.Invitation.InviteeEmail,
            source.Invitation.Code,
            source.Invitation.Status,
            source.Invitation.ExpiresAt,
            source.Invitation.RespondedAt,
            source.Invitation.CreatedAt,
            source.Trainee?.Name,
            source.Trainee?.Email));
        configuration.CreateMap<PublicInvitationStatusSource, PublicInvitationStatusReadModel>((source, _) => new PublicInvitationStatusReadModel(
            source.Invitation.Status,
            source.UserExists));
    }
}

internal sealed record InvitationListSource(
    CoachingInvitationFact Invitation,
    LgymApi.Domain.Enums.TrainerInvitationStatus Status,
    DateTimeOffset? RespondedAt);

internal sealed record InvitationWithAccountSource(CoachingInvitationFact Invitation, AccountReadModel? Trainee);

internal sealed record PublicInvitationStatusSource(CoachingInvitationFact Invitation, bool UserExists);

internal sealed record InvitationCreationSource(
    Id<TrainerInvitationEntity> Id,
    Id<UserEntity> TrainerId,
    string InviteeEmail,
    Id<UserEntity>? TraineeId,
    string Code,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

internal sealed record InvitationResponseSource(
    Id<TrainerInvitationEntity> Id,
    Id<UserEntity>? TraineeId,
    TrainerInvitationStatus Status,
    DateTimeOffset RespondedAt);

internal sealed record InvitationActiveLinkSource(
    Id<LgymApi.Domain.Entities.TrainerTraineeLink> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);
