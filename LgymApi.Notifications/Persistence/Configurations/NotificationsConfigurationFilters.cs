namespace LgymApi.Notifications.Persistence.Configurations;

internal static class NotificationsConfigurationFilters
{
    internal const string ActiveRowsFilter = "\"IsDeleted\" = FALSE";
    internal const string ActiveDeliveryKeyFilter = ActiveRowsFilter + " AND \"DeliveryKey\" IS NOT NULL";
}
