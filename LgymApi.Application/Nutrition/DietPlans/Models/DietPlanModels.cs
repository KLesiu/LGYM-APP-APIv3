using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.DietPlans.Models;

public sealed record DietPlanUpsertData
{
    public DietPlanUpsertData(
        string name,
        DateOnly startDate,
        DateOnly? endDate,
        int? estimatedCalories,
        decimal? proteinGrams,
        decimal? carbsGrams,
        decimal? fatGrams,
        string? notes,
        bool isActive,
        IEnumerable<DietMealInput> meals)
    {
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        EstimatedCalories = estimatedCalories;
        ProteinGrams = proteinGrams;
        CarbsGrams = carbsGrams;
        FatGrams = fatGrams;
        Notes = notes;
        IsActive = isActive;
        Meals = Array.AsReadOnly(meals.ToArray());
    }

    public string Name { get; }
    public DateOnly StartDate { get; }
    public DateOnly? EndDate { get; }
    public int? EstimatedCalories { get; }
    public decimal? ProteinGrams { get; }
    public decimal? CarbsGrams { get; }
    public decimal? FatGrams { get; }
    public string? Notes { get; }
    public bool IsActive { get; }
    public IReadOnlyList<DietMealInput> Meals { get; }
}

public sealed record DietMealInput(
    string Name,
    int Order,
    string? Description,
    int? EstimatedCalories,
    decimal? ProteinGrams,
    decimal? CarbsGrams,
    decimal? FatGrams);

public sealed record DietPlanReadModel
{
    public DietPlanReadModel(
        Id<DietPlan> id,
        Id<UserEntity> trainerId,
        Id<UserEntity> traineeId,
        string name,
        DateOnly startDate,
        DateOnly? endDate,
        int? estimatedCalories,
        decimal? proteinGrams,
        decimal? carbsGrams,
        decimal? fatGrams,
        string? notes,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<DietMealReadModel> meals)
    {
        Id = id;
        TrainerId = trainerId;
        TraineeId = traineeId;
        Name = name;
        StartDate = startDate;
        EndDate = endDate;
        EstimatedCalories = estimatedCalories;
        ProteinGrams = proteinGrams;
        CarbsGrams = carbsGrams;
        FatGrams = fatGrams;
        Notes = notes;
        IsActive = isActive;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Meals = Array.AsReadOnly(meals.ToArray());
    }

    public Id<DietPlan> Id { get; }
    public Id<UserEntity> TrainerId { get; }
    public Id<UserEntity> TraineeId { get; }
    public string Name { get; }
    public DateOnly StartDate { get; }
    public DateOnly? EndDate { get; }
    public int? EstimatedCalories { get; }
    public decimal? ProteinGrams { get; }
    public decimal? CarbsGrams { get; }
    public decimal? FatGrams { get; }
    public string? Notes { get; }
    public bool IsActive { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
    public IReadOnlyList<DietMealReadModel> Meals { get; }
}

public sealed record DietMealReadModel(
    Id<DietMeal> Id,
    string Name,
    int Order,
    string? Description,
    int? EstimatedCalories,
    decimal? ProteinGrams,
    decimal? CarbsGrams,
    decimal? FatGrams);

public sealed record DietPlanHistoryReadModel(
    Id<DietPlanHistory> Id,
    Id<DietPlan> DietPlanId,
    Id<UserEntity> ChangedByUserId,
    DateTimeOffset ChangeDate,
    string ChangeType,
    string SnapshotJson);

internal sealed record NormalizedDietPlanData(
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate,
    int? EstimatedCalories,
    decimal? ProteinGrams,
    decimal? CarbsGrams,
    decimal? FatGrams,
    string? Notes,
    bool IsActive,
    IReadOnlyList<DietMealInput> Meals);
