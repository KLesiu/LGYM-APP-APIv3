using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Nutrition.Errors;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.Nutrition.Supplementation;

internal static class SupplementationRules
{
    private const int PlanNameMaxLength = 120;
    private const int PlanNotesMaxLength = 1000;
    private const int SupplementNameMaxLength = 160;
    private const int DosageMaxLength = 120;
    public const int MaxComplianceRangeDays = 366;

    public static AppError? GetUpsertError(SupplementPlanUpsertData data)
    {
        if (string.IsNullOrWhiteSpace(data.Name)
            || data.Name.Trim().Length > PlanNameMaxLength
            || data.Items.Count == 0
            || data.Notes?.Trim().Length > PlanNotesMaxLength)
        {
            return new InvalidSupplementationError(Messages.FieldRequired);
        }

        foreach (var item in data.Items)
        {
            if (string.IsNullOrWhiteSpace(item.SupplementName)
                || string.IsNullOrWhiteSpace(item.Dosage)
                || string.IsNullOrWhiteSpace(item.TimeOfDay)
                || item.SupplementName.Trim().Length > SupplementNameMaxLength
                || item.Dosage.Trim().Length > DosageMaxLength
                || item.DaysOfWeekMask is < 1 or > 127
                || !TimeOnly.TryParse(item.TimeOfDay, out _))
            {
                return new InvalidSupplementationError(Messages.FieldRequired);
            }
        }

        return null;
    }

    public static NormalizedSupplementPlanData Normalize(SupplementPlanUpsertData data)
    {
        var items = data.Items
            .Select(item =>
            {
                TimeOnly.TryParse(item.TimeOfDay, out var timeOfDay);
                return new NormalizedSupplementPlanItemData(
                    item.SupplementName.Trim(),
                    item.Dosage.Trim(),
                    timeOfDay.ToTimeSpan(),
                    (DaysOfWeekSet)item.DaysOfWeekMask,
                    item.Order);
            })
            .OrderBy(item => item.Order)
            .ThenBy(item => item.TimeOfDay)
            .ThenBy(item => item.SupplementName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NormalizedSupplementPlanData(
            data.Name.Trim(),
            string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim(),
            Array.AsReadOnly(items));
    }

    public static bool IsScheduledOnDate(DaysOfWeekSet daysOfWeekMask, DateOnly date)
    {
        var normalizedDay = ((int)date.DayOfWeek + 6) % 7;
        return ((int)daysOfWeekMask & (1 << normalizedDay)) != 0;
    }

    public static string FormatTime(TimeSpan value)
        => TimeOnly.FromTimeSpan(value).ToString("HH:mm");

    public static AppError? GetComplianceRangeError(DateOnly fromDate, DateOnly toDate)
    {
        if (toDate < fromDate)
        {
            return new InvalidSupplementationError(Messages.InvalidDateRange);
        }

        return toDate.DayNumber - fromDate.DayNumber + 1 > MaxComplianceRangeDays
            ? new InvalidSupplementationError(Messages.DateRangeTooLarge)
            : null;
    }
}
