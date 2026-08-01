using LgymApi.Application.Coaching.Persistence;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes;

public sealed class TraineeNoteMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<CoachingTraineeNoteFact, TraineeNoteReadModel>((source, _) => new TraineeNoteReadModel(
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
        configuration.CreateMap<CoachingTraineeNoteHistoryFact, TraineeNoteHistoryReadModel>((source, _) => new TraineeNoteHistoryReadModel(
            source.Id,
            source.TraineeNoteId,
            source.ChangedByUserId,
            source.ChangedAt,
            source.PreviousContent,
            source.NewContent,
            source.ChangeType));
        configuration.CreateMap<CreateTraineeNoteSource, CoachingTraineeNoteWriteModel>((source, _) => new CoachingTraineeNoteWriteModel(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            NormalizeNullable(source.Data.Title),
            source.Data.Content.Trim(),
            source.Data.VisibleToTrainee,
            source.Data.IsPinned,
            source.TrainerId,
            source.LastUpdatedAt,
            false));
        configuration.CreateMap<UpdateTraineeNoteSource, CoachingTraineeNoteWriteModel>((source, _) => new CoachingTraineeNoteWriteModel(
            source.Note.Id,
            source.Note.TrainerId,
            source.Note.TraineeId,
            NormalizeNullable(source.Data.Title),
            source.Data.Content.Trim(),
            source.Data.VisibleToTrainee,
            source.Data.IsPinned,
            source.ChangedByUserId,
            source.LastUpdatedAt,
            false));
        configuration.CreateMap<DeleteTraineeNoteSource, CoachingTraineeNoteWriteModel>((source, _) => new CoachingTraineeNoteWriteModel(
            source.Note.Id,
            source.Note.TrainerId,
            source.Note.TraineeId,
            source.Note.Title,
            source.Note.Content,
            false,
            source.Note.IsPinned,
            source.ChangedByUserId,
            source.LastUpdatedAt,
            true));
        configuration.CreateMap<TraineeNoteHistorySource, CoachingTraineeNoteHistoryWriteModel>((source, _) => new CoachingTraineeNoteHistoryWriteModel(
            source.Id,
            source.TraineeNoteId,
            source.ChangedByUserId,
            source.ChangedAt,
            source.PreviousContent,
            source.NewContent,
            source.ChangeType));
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record CreateTraineeNoteSource(
    Id<TraineeNoteEntity> Id,
    Id<UserEntity> TrainerId,
    Id<UserEntity> TraineeId,
    TraineeNoteUpsertData Data,
    DateTimeOffset LastUpdatedAt);

internal sealed record UpdateTraineeNoteSource(
    CoachingTraineeNoteFact Note,
    Id<UserEntity> ChangedByUserId,
    TraineeNoteUpsertData Data,
    DateTimeOffset LastUpdatedAt);

internal sealed record DeleteTraineeNoteSource(
    CoachingTraineeNoteFact Note,
    Id<UserEntity> ChangedByUserId,
    DateTimeOffset LastUpdatedAt);

internal sealed record TraineeNoteHistorySource(
    Id<TraineeNoteHistoryEntity> Id,
    Id<TraineeNoteEntity> TraineeNoteId,
    Id<UserEntity> ChangedByUserId,
    DateTimeOffset ChangedAt,
    string? PreviousContent,
    string NewContent,
    string ChangeType);
