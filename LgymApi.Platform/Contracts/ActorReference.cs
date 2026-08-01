using System.Diagnostics.CodeAnalysis;

namespace LgymApi.Platform.Contracts;

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class ActorReference
{
    private ActorReference()
    {
    }
}
