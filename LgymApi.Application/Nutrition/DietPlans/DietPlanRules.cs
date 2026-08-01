using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.DietPlans;

internal static class DietPlanRules
{
    public static AppError? GetUpsertError(DietPlanUpsertData data)
    {
        if (string.IsNullOrWhiteSpace(data.Name) || data.StartDate == default)
        {
            return new BadRequestError(Messages.FieldRequired);
        }

        if (data.Meals.Count == 0)
        {
            return HasAnyDietTargets(data) ? null : new BadRequestError(Messages.FieldRequired);
        }

        return data.Meals.Any(meal => string.IsNullOrWhiteSpace(meal.Name))
            ? new BadRequestError(Messages.FieldRequired)
            : null;
    }

    public static NormalizedDietPlanData Normalize(DietPlanUpsertData data)
    {
        var meals = data.Meals
            .Select((meal, index) => new DietMealInput(
                meal.Name.Trim(),
                meal.Order < 0 ? index : meal.Order,
                NormalizeNullable(meal.Description),
                meal.EstimatedCalories,
                meal.ProteinGrams,
                meal.CarbsGrams,
                meal.FatGrams))
            .OrderBy(meal => meal.Order)
            .ToArray();

        return new NormalizedDietPlanData(
            data.Name.Trim(),
            data.StartDate,
            data.EndDate,
            data.EstimatedCalories,
            data.ProteinGrams,
            data.CarbsGrams,
            data.FatGrams,
            NormalizeNullable(data.Notes),
            data.IsActive,
            Array.AsReadOnly(meals));
    }

    private static bool HasAnyDietTargets(DietPlanUpsertData data)
        => data.EstimatedCalories.HasValue
           || data.ProteinGrams.HasValue
           || data.CarbsGrams.HasValue
           || data.FatGrams.HasValue;

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
