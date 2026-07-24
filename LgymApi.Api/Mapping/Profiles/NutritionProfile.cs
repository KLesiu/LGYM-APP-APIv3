using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class NutritionProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<UpsertDietMealRequest, DietMealInput>((source, _) => new DietMealInput(
            source.Name,
            source.Order,
            source.Description,
            source.EstimatedCalories,
            source.ProteinGrams,
            source.CarbsGrams,
            source.FatGrams));

        configuration.CreateMap<UpsertDietPlanRequest, DietPlanUpsertData>((source, context) => new DietPlanUpsertData(
            source.Name,
            source.StartDate,
            source.EndDate,
            source.EstimatedCalories,
            source.ProteinGrams,
            source.CarbsGrams,
            source.FatGrams,
            source.Notes,
            source.IsActive,
            context!.MapList<DietMealInput>(source.Meals)));

        configuration.CreateMap<UpsertSupplementPlanItemRequest, SupplementPlanItemInput>((source, _) => new SupplementPlanItemInput(
            source.SupplementName,
            source.Dosage,
            source.TimeOfDay,
            source.DaysOfWeekMask,
            source.Order));

        configuration.CreateMap<UpsertSupplementPlanRequest, SupplementPlanUpsertData>((source, context) => new SupplementPlanUpsertData(
            source.Name,
            source.Notes,
            context!.MapList<SupplementPlanItemInput>(source.Items)));

        configuration.CreateMap<DietMealReadModel, DietMealDto>((source, _) => new DietMealDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Order = source.Order,
            Description = source.Description,
            EstimatedCalories = source.EstimatedCalories,
            ProteinGrams = source.ProteinGrams,
            CarbsGrams = source.CarbsGrams,
            FatGrams = source.FatGrams
        });

        configuration.CreateMap<DietPlanReadModel, DietPlanDto>((source, context) => new DietPlanDto
        {
            Id = source.Id.ToString(),
            TrainerId = source.TrainerId.ToString(),
            TraineeId = source.TraineeId.ToString(),
            Name = source.Name,
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            EstimatedCalories = source.EstimatedCalories,
            ProteinGrams = source.ProteinGrams,
            CarbsGrams = source.CarbsGrams,
            FatGrams = source.FatGrams,
            Notes = source.Notes,
            IsActive = source.IsActive,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            Meals = context!.MapList<DietMealDto>(source.Meals)
        });

        configuration.CreateMap<DietPlanHistoryReadModel, DietPlanHistoryDto>((source, _) => new DietPlanHistoryDto
        {
            Id = source.Id.ToString(),
            DietPlanId = source.DietPlanId.ToString(),
            ChangedByUserId = source.ChangedByUserId.ToString(),
            ChangeDate = source.ChangeDate,
            ChangeType = source.ChangeType,
            SnapshotJson = source.SnapshotJson
        });

        configuration.CreateMap<SupplementPlanItemReadModel, SupplementPlanItemDto>((source, _) => new SupplementPlanItemDto
        {
            Id = source.Id.ToString(),
            SupplementName = source.SupplementName,
            Dosage = source.Dosage,
            TimeOfDay = source.TimeOfDay,
            DaysOfWeekMask = source.DaysOfWeekMask,
            Order = source.Order
        });

        configuration.CreateMap<SupplementPlanReadModel, SupplementPlanDto>((source, context) => new SupplementPlanDto
        {
            Id = source.Id.ToString(),
            TrainerId = source.TrainerId.ToString(),
            TraineeId = source.TraineeId.ToString(),
            Name = source.Name,
            Notes = source.Notes,
            IsActive = source.IsActive,
            CreatedAt = source.CreatedAt,
            Items = context!.MapList<SupplementPlanItemDto>(source.Items)
        });

        configuration.CreateMap<SupplementScheduleEntryReadModel, SupplementScheduleEntryDto>((source, _) => new SupplementScheduleEntryDto
        {
            PlanItemId = source.PlanItemId.ToString(),
            SupplementName = source.SupplementName,
            Dosage = source.Dosage,
            TimeOfDay = source.TimeOfDay,
            IntakeDate = source.IntakeDate,
            Taken = source.Taken,
            TakenAt = source.TakenAt
        });

        configuration.CreateMap<SupplementComplianceSummaryReadModel, SupplementComplianceSummaryDto>((source, _) => new SupplementComplianceSummaryDto
        {
            TraineeId = source.TraineeId.ToString(),
            FromDate = source.FromDate,
            ToDate = source.ToDate,
            PlannedDoses = source.PlannedDoses,
            TakenDoses = source.TakenDoses,
            AdherenceRate = source.AdherenceRate
        });
    }
}
