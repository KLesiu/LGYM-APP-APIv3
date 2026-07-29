using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.WorkoutProgress.ReportingIntegration;

public sealed record AcceptedReportMeasurementPersistenceModel(
    Id<AccountReference> TraineeId,
    BodyParts BodyPart,
    MeasurementUnits Unit,
    double Value,
    DateTimeOffset CreatedAt);

public interface IReportSubmissionAcceptedProgressPersistence
{
    Task<IReadOnlySet<BodyParts>> GetExistingBodyPartsAsync(
        Id<AccountReference> traineeId,
        IReadOnlyCollection<BodyParts> bodyParts,
        DateTimeOffset createdAtFromUtc,
        DateTimeOffset createdAtToUtc,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        AcceptedReportMeasurementPersistenceModel measurement,
        CancellationToken cancellationToken = default);
}
