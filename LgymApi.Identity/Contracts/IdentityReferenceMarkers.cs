using System.Diagnostics.CodeAnalysis;

namespace LgymApi.Identity.Contracts;

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class AccountReference
{
    private AccountReference()
    {
    }
}

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class AccountSessionReference
{
    private AccountSessionReference()
    {
    }
}

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class RoleReference
{
    private RoleReference()
    {
    }
}
