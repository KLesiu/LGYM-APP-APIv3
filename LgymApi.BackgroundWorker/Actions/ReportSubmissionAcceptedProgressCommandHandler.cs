using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class ReportSubmissionAcceptedProgressCommandHandler :
    IBackgroundAction<ReportSubmissionAcceptedProgressCommand>
{
    private readonly IReportSubmissionAcceptedProgressActionExecutionPort _port;

    public ReportSubmissionAcceptedProgressCommandHandler(
        IReportSubmissionAcceptedProgressActionExecutionPort port)
    {
        _port = port ?? throw new ArgumentNullException(nameof(port));
    }

    public Task ExecuteAsync(
        ReportSubmissionAcceptedProgressCommand command,
        CancellationToken cancellationToken = default)
        => _port.ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
}
