using LgymApi.Domain.ValueObjects;
using TraineeNoteEntity = LgymApi.Domain.Entities.TraineeNote;
using TraineeNoteHistoryEntity = LgymApi.Domain.Entities.TraineeNoteHistory;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Coaching.TraineeNotes.Models;

public sealed record TraineeNoteUpsertData(
    string? Title,
    string Content,
    bool VisibleToTrainee,
    bool IsPinned);

public sealed record TraineeNoteReadModel(
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

public sealed record TraineeNoteHistoryReadModel(
    Id<TraineeNoteHistoryEntity> Id,
    Id<TraineeNoteEntity> TraineeNoteId,
    Id<UserEntity> ChangedByUserId,
    DateTimeOffset ChangedAt,
    string? PreviousContent,
    string NewContent,
    string ChangeType);
