using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation.Models;

public sealed record SupplementPlanUpsertData
{
    public SupplementPlanUpsertData(string name, string? notes, IEnumerable<SupplementPlanItemInput> items)
    {
        Name = name;
        Notes = notes;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public string Name { get; }
    public string? Notes { get; }
    public IReadOnlyList<SupplementPlanItemInput> Items { get; }
}

public sealed record SupplementPlanItemInput(
    string SupplementName,
    string Dosage,
    string TimeOfDay,
    int DaysOfWeekMask,
    int Order);

public sealed record SupplementPlanReadModel
{
    public SupplementPlanReadModel(
        Id<SupplementPlan> id,
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId,
        string name,
        string? notes,
        bool isActive,
        DateTimeOffset createdAt,
        IEnumerable<SupplementPlanItemReadModel> items)
    {
        Id = id;
        TrainerId = trainerId;
        TraineeId = traineeId;
        Name = name;
        Notes = notes;
        IsActive = isActive;
        CreatedAt = createdAt;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public Id<SupplementPlan> Id { get; }
    public Id<UserEntity> TrainerId { get; }
    public Id<UserEntity> TraineeId { get; }
    public string Name { get; }
    public string? Notes { get; }
    public bool IsActive { get; }
    public DateTimeOffset CreatedAt { get; }
    public IReadOnlyList<SupplementPlanItemReadModel> Items { get; }
}

public sealed record SupplementPlanItemReadModel(
    Id<SupplementPlanItem> Id,
    string SupplementName,
    string Dosage,
    string TimeOfDay,
    int DaysOfWeekMask,
    int Order);

public sealed record SupplementScheduleEntryReadModel(
    Id<SupplementPlanItem> PlanItemId,
    string SupplementName,
    string Dosage,
    string TimeOfDay,
    DateOnly IntakeDate,
    bool Taken,
    DateTimeOffset? TakenAt);

public sealed record SupplementComplianceSummaryReadModel(
    Id<UserEntity> TraineeId,
    DateOnly FromDate,
    DateOnly ToDate,
    int PlannedDoses,
    int TakenDoses,
    double AdherenceRate);

internal sealed record NormalizedSupplementPlanData(
    string Name,
    string? Notes,
    IReadOnlyList<NormalizedSupplementPlanItemData> Items);

internal sealed record NormalizedSupplementPlanItemData(
    string SupplementName,
    string Dosage,
    TimeSpan TimeOfDay,
    DaysOfWeekSet DaysOfWeekMask,
    int Order);
