using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Nutrition.Supplementation;

internal static class SupplementComplianceProjector
{
    public static SupplementComplianceSummaryReadModel Project(
        Id<UserEntity> traineeId,
        IEnumerable<SupplementPlanItem> planItems,
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyCollection<SupplementIntakeLog> intakeLogs)
    {
        var items = planItems.ToArray();
        var plannedDoses = 0;
        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            plannedDoses += items.Count(item => SupplementationRules.IsScheduledOnDate(item.DaysOfWeekMask, date));
        }

        var takenDoses = intakeLogs.Count;
        var adherenceRate = plannedDoses == 0
            ? 0
            : Math.Round((double)takenDoses / plannedDoses * 100, 2);

        return new SupplementComplianceSummaryReadModel(
            traineeId,
            fromDate,
            toDate,
            plannedDoses,
            takenDoses,
            adherenceRate);
    }
}
