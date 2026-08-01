using System.Diagnostics.CodeAnalysis;

namespace LgymApi.Notifications.Contracts;

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class NotificationReference
{
    private NotificationReference()
    {
    }
}

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class PushInstallationReference
{
    private PushInstallationReference()
    {
    }
}
