using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Application.Coaching.Relationships.GetCurrentTrainer;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class TraineeCoachingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<UpsertTraineeNoteRequest, CreateTraineeNoteCommand>((source, _) => new CreateTraineeNoteCommand(
            Id<UserEntity>.Empty,
            Id<UserEntity>.Empty,
            new TraineeNoteUpsertData(source.Title, source.Content, source.VisibleToTrainee, source.IsPinned)));
        configuration.CreateMap<UpsertTraineeNoteRequest, UpdateTraineeNoteCommand>((source, _) => new UpdateTraineeNoteCommand(
            Id<UserEntity>.Empty,
            Id<UserEntity>.Empty,
            Id<LgymApi.Domain.Entities.TraineeNote>.Empty,
            new TraineeNoteUpsertData(source.Title, source.Content, source.VisibleToTrainee, source.IsPinned)));
        configuration.CreateMap<CurrentTrainerReadModel, TraineeTrainerProfileDto>((source, _) => new TraineeTrainerProfileDto
        {
            TrainerId = source.TrainerId.ToString(),
            Name = source.Name,
            Email = source.Email,
            Avatar = source.Avatar,
            LinkedAt = source.LinkedAt
        });
        configuration.CreateMap<TraineeNoteReadModel, TraineeNoteDto>((source, _) => new TraineeNoteDto
        {
            Id = source.Id.ToString(),
            TrainerId = source.TrainerId.ToString(),
            TraineeId = source.TraineeId.ToString(),
            Title = source.Title,
            Content = source.Content,
            VisibleToTrainee = source.VisibleToTrainee,
            IsPinned = source.IsPinned,
            LastUpdatedByUserId = source.LastUpdatedByUserId.ToString(),
            LastUpdatedAt = source.LastUpdatedAt,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt
        });
        configuration.CreateMap<TraineeNoteHistoryReadModel, TraineeNoteHistoryDto>((source, _) => new TraineeNoteHistoryDto
        {
            Id = source.Id.ToString(),
            TraineeNoteId = source.TraineeNoteId.ToString(),
            ChangedByUserId = source.ChangedByUserId.ToString(),
            ChangedAt = source.ChangedAt,
            PreviousContent = source.PreviousContent,
            NewContent = source.NewContent,
            ChangeType = source.ChangeType
        });
    }
}
