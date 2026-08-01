namespace LgymApi.Infrastructure.Data.Configurations.Reporting;

internal static class ReportingConfigurationFilters
{
    internal const string ActiveRowsFilter = "\"IsDeleted\" = FALSE";
    internal const string ActiveCurrentReportRequestFilter = "\"CurrentReportRequestId\" IS NOT NULL AND \"IsDeleted\" = FALSE";
}
