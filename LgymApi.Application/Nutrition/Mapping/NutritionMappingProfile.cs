using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Nutrition.Mapping;

public sealed class NutritionMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<NormalizedDietPlanData, DietPlan>((source, context) => new DietPlan
        {
            Name = source.Name,
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            EstimatedCalories = source.EstimatedCalories,
            ProteinGrams = source.ProteinGrams,
            CarbsGrams = source.CarbsGrams,
            FatGrams = source.FatGrams,
            Notes = source.Notes,
            IsActive = source.IsActive,
            Meals = context!.MapList<DietMealInput, DietMeal>(source.Meals)
        });
        configuration.CreateMap<DietMealInput, DietMeal>((source, _) => new DietMeal
        {
            Name = source.Name,
            Order = source.Order,
            Description = source.Description,
            EstimatedCalories = source.EstimatedCalories,
            ProteinGrams = source.ProteinGrams,
            CarbsGrams = source.CarbsGrams,
            FatGrams = source.FatGrams
        });
        configuration.CreateMap<DietPlan, DietPlanReadModel>((source, context) => new DietPlanReadModel(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.Name,
            source.StartDate,
            source.EndDate,
            source.EstimatedCalories,
            source.ProteinGrams,
            source.CarbsGrams,
            source.FatGrams,
            source.Notes,
            source.IsActive,
            source.CreatedAt,
            source.UpdatedAt,
            context!.MapList<DietMeal, DietMealReadModel>(source.Meals.OrderBy(meal => meal.Order))));
        configuration.CreateMap<DietMeal, DietMealReadModel>((source, _) => new DietMealReadModel(
            source.Id,
            source.Name,
            source.Order,
            source.Description,
            source.EstimatedCalories,
            source.ProteinGrams,
            source.CarbsGrams,
            source.FatGrams));
        configuration.CreateMap<DietPlanHistory, DietPlanHistoryReadModel>((source, _) => new DietPlanHistoryReadModel(
            source.Id,
            source.DietPlanId,
            source.ChangedByUserId,
            source.ChangeDate,
            source.ChangeType,
            source.SnapshotJson));

        configuration.CreateMap<NormalizedSupplementPlanData, SupplementPlan>((source, context) => new SupplementPlan
        {
            Name = source.Name,
            Notes = source.Notes,
            Items = context!.MapList<NormalizedSupplementPlanItemData, SupplementPlanItem>(source.Items)
        });
        configuration.CreateMap<NormalizedSupplementPlanItemData, SupplementPlanItem>((source, _) => new SupplementPlanItem
        {
            SupplementName = source.SupplementName,
            Dosage = source.Dosage,
            TimeOfDay = source.TimeOfDay,
            DaysOfWeekMask = source.DaysOfWeekMask,
            Order = source.Order
        });
        configuration.CreateMap<SupplementPlan, SupplementPlanReadModel>((source, context) => new SupplementPlanReadModel(
            source.Id,
            source.TrainerId,
            source.TraineeId,
            source.Name,
            source.Notes,
            source.IsActive,
            source.CreatedAt,
            context!.MapList<SupplementPlanItem, SupplementPlanItemReadModel>(source.Items
                .OrderBy(item => item.Order)
                .ThenBy(item => item.TimeOfDay)
                .ThenBy(item => item.CreatedAt))));
        configuration.CreateMap<SupplementPlanItem, SupplementPlanItemReadModel>((source, _) => new SupplementPlanItemReadModel(
            source.Id,
            source.SupplementName,
            source.Dosage,
            Supplementation.SupplementationRules.FormatTime(source.TimeOfDay),
            (int)source.DaysOfWeekMask,
            source.Order));
    }
}
