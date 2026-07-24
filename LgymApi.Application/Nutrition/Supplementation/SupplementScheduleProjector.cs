using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;

namespace LgymApi.Application.Nutrition.Supplementation;

internal static class SupplementScheduleProjector
{
    public static IReadOnlyList<SupplementScheduleEntryReadModel> Project(
        SupplementPlan plan,
        DateOnly intakeDate,
        IReadOnlyCollection<SupplementIntakeLog> intakeLogs)
    {
        var logsByPlanItem = intakeLogs.ToDictionary(log => log.PlanItemId);

        return plan.Items
            .Where(item => SupplementationRules.IsScheduledOnDate(item.DaysOfWeekMask, intakeDate))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.TimeOfDay)
            .ThenBy(item => item.CreatedAt)
            .Select(item =>
            {
                var taken = logsByPlanItem.TryGetValue(item.Id, out var intakeLog);
                return new SupplementScheduleEntryReadModel(
                    item.Id,
                    item.SupplementName,
                    item.Dosage,
                    SupplementationRules.FormatTime(item.TimeOfDay),
                    intakeDate,
                    taken,
                    taken ? intakeLog!.TakenAt : null);
            })
            .ToArray();
    }
}
