using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions.Contracts;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

public sealed partial class ReportRequestCreatedInAppNotificationCommandHandler : IBackgroundAction<ReportRequestCreatedInAppNotificationCommand>
{
    private readonly IReportRequestCreatedActionExecutionPort _executionPort;

    public ReportRequestCreatedInAppNotificationCommandHandler(
        IReportRequestCreatedActionExecutionPort executionPort)
    {
        _executionPort = executionPort ?? throw new ArgumentNullException(nameof(executionPort));
    }

    public async Task ExecuteAsync(ReportRequestCreatedInAppNotificationCommand command, CancellationToken cancellationToken = default)
    {
        await _executionPort.ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
    }
}
