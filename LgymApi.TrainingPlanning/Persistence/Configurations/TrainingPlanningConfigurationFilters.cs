namespace LgymApi.TrainingPlanning.Persistence.Configurations;

internal static class TrainingPlanningConfigurationFilters
{
    internal const string ActiveRowsFilter = "\"IsDeleted\" = FALSE";
    internal const string ActiveShareCodeFilter = ActiveRowsFilter + " AND \"ShareCode\" IS NOT NULL";
}
