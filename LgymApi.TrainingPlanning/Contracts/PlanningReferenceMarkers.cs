using System.Diagnostics.CodeAnalysis;

namespace LgymApi.TrainingPlanning.Contracts;

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class PlanReference
{
    private PlanReference()
    {
    }
}

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class PlanDayReference
{
    private PlanDayReference()
    {
    }
}

[SuppressMessage("Major Bug", "S3453", Justification = "Contract-only Id<T> marker; construction is intentionally prohibited and enforced by ReferenceMarkerPersistenceGuardTests.")]
public sealed class PlanExerciseReference
{
    private PlanExerciseReference()
    {
    }
}
