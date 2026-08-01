using LgymApi.Application.Mapping.Core;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;
using TrainerInvitationEntity = LgymApi.Domain.Entities.TrainerInvitation;
using TrainerTraineeLinkEntity = LgymApi.Domain.Entities.TrainerTraineeLink;

namespace LgymApi.Application.Coaching.Persistence;

public sealed class CoachingPersistenceMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<CoachingInvitationWriteModel, TrainerInvitationEntity>((source, _) => new TrainerInvitationEntity
        {
            Id = source.Id,
            TrainerId = source.TrainerId,
            InviteeEmail = source.InviteeEmail,
            TraineeId = source.TraineeId,
            Code = source.Code,
            Status = source.Status,
            ExpiresAt = source.ExpiresAt,
            CreatedAt = source.CreatedAt,
            RespondedAt = source.RespondedAt
        });
        configuration.CreateMap<TrainerInvitationEntity, CoachingInvitationFact>((source, _) => new CoachingInvitationFact(
            source.Id,
            source.TrainerId,
            source.InviteeEmail,
            source.TraineeId,
            source.Code,
            source.Status,
            source.ExpiresAt,
            source.RespondedAt,
            source.CreatedAt,
            source.UpdatedAt));
        configuration.CreateMap<CoachingInvitationResponseUpdateModel, TrainerInvitationEntity>((source, _) => new TrainerInvitationEntity
        {
            Id = source.Id,
            TraineeId = source.TraineeId,
            Status = source.Status,
            RespondedAt = source.RespondedAt
        });

        configuration.CreateMap<CoachingActiveLinkWriteModel, TrainerTraineeLinkEntity>((source, _) => new TrainerTraineeLinkEntity
        {
            Id = source.Id,
            TrainerId = source.TrainerId,
            TraineeId = source.TraineeId
        });
        configuration.CreateMap<TrainerTraineeLinkEntity, CoachingActiveLinkFact>((source, _) => new CoachingActiveLinkFact(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.CreatedAt,
            source.UpdatedAt));
        configuration.CreateMap<CoachingDashboardSource, CoachingDashboardFact>((source, _) => new CoachingDashboardFact(
            source.TraineeId,
            source.ActiveLink,
            source.LatestInvitation));

        configuration.CreateMap<CoachingTraineeNoteWriteModel, TraineeNoteEntity>((source, _) => new TraineeNoteEntity
        {
            Id = source.Id,
            TrainerId = source.TrainerId,
            TraineeId = source.TraineeId,
            Title = source.Title,
            Content = source.Content,
            VisibleToTrainee = source.VisibleToTrainee,
            IsPinned = source.IsPinned,
            LastUpdatedByUserId = source.LastUpdatedByUserId,
            LastUpdatedAt = source.LastUpdatedAt,
            IsDeleted = source.IsDeleted
        });
        configuration.CreateMap<TraineeNoteEntity, CoachingTraineeNoteFact>((source, _) => new CoachingTraineeNoteFact(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.Title,
            source.Content,
            source.VisibleToTrainee,
            source.IsPinned,
            source.LastUpdatedByUserId,
            source.LastUpdatedAt,
            source.CreatedAt,
            source.UpdatedAt));

        configuration.CreateMap<CoachingTraineeNoteHistoryWriteModel, TraineeNoteHistoryEntity>((source, _) => new TraineeNoteHistoryEntity
        {
            Id = source.Id,
            TraineeNoteId = source.TraineeNoteId,
            ChangedByUserId = source.ChangedByUserId,
            ChangedAt = source.ChangedAt,
            PreviousContent = source.PreviousContent,
            NewContent = source.NewContent,
            ChangeType = source.ChangeType
        });
        configuration.CreateMap<TraineeNoteHistoryEntity, CoachingTraineeNoteHistoryFact>((source, _) => new CoachingTraineeNoteHistoryFact(
            source.Id,
            source.TraineeNoteId,
            source.ChangedByUserId,
            source.ChangedAt,
            source.PreviousContent,
            source.NewContent,
            source.ChangeType,
            source.CreatedAt,
            source.UpdatedAt));
    }
}
