namespace LgymApi.Application.WorkoutProgress.Contracts.ReportingIntegration;

public interface IReportSubmissionAcceptedProgressConsumer
{
    Task<ReportSubmissionAcceptedProgressConsumeResult> ConsumeAsync(
        ReportSubmissionAcceptedProgressEvent @event,
        CancellationToken cancellationToken = default);
}
