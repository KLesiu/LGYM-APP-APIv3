using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using TrainerTraineeLinkEntity = LgymApi.Domain.Entities.TrainerTraineeLink;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.Persistence;

public sealed record CoachingInvitationWriteModel(
    Id<TrainerInvitationEntity> Id,
    Id<UserEntity> TrainerId,
    string InviteeEmail,
    Id<UserEntity>? TraineeId,
    string Code,
    TrainerInvitationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RespondedAt);

public sealed record CoachingInvitationFact(
    Id<TrainerInvitationEntity> Id,
    Id<UserEntity> TrainerId,
    string InviteeEmail,
    Id<UserEntity>? TraineeId,
    string Code,
    TrainerInvitationStatus Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RespondedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CoachingInvitationResponseUpdateModel(
    Id<TrainerInvitationEntity> Id,
    Id<UserEntity>? TraineeId,
    TrainerInvitationStatus Status,
    DateTimeOffset RespondedAt);

public sealed record CoachingActiveLinkWriteModel(
    Id<TrainerTraineeLinkEntity> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId);

public sealed record CoachingActiveLinkFact(
    Id<TrainerTraineeLinkEntity> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CoachingDashboardFact(
    Id<UserEntity> TraineeId,
    CoachingActiveLinkFact? ActiveLink,
    CoachingInvitationFact? LatestInvitation);

public sealed record CoachingDashboardSource(
    Id<UserEntity> TraineeId,
    CoachingActiveLinkFact? ActiveLink,
    CoachingInvitationFact? LatestInvitation);

public sealed record CoachingTraineeNoteWriteModel(
    Id<TraineeNoteEntity> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    string? Title,
    string Content,
    bool VisibleToTrainee,
    bool IsPinned,
    Id<UserEntity> LastUpdatedByUserId,
    DateTimeOffset LastUpdatedAt,
    bool IsDeleted = false);

public sealed record CoachingTraineeNoteHistoryWriteModel(
    Id<TraineeNoteHistoryEntity> Id,
    Id<TraineeNoteEntity> TraineeNoteId,
    Id<UserEntity> ChangedByUserId,
    DateTimeOffset ChangedAt,
    string? PreviousContent,
    string NewContent,
    string ChangeType);

public sealed record CoachingTraineeNoteFact(
    Id<TraineeNoteEntity> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    string? Title,
    string Content,
    bool VisibleToTrainee,
    bool IsPinned,
    Id<UserEntity> LastUpdatedByUserId,
    DateTimeOffset LastUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CoachingTraineeNoteHistoryFact(
    Id<TraineeNoteHistoryEntity> Id,
    Id<TraineeNoteEntity> TraineeNoteId,
    Id<UserEntity> ChangedByUserId,
    DateTimeOffset ChangedAt,
    string? PreviousContent,
    string NewContent,
    string ChangeType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
