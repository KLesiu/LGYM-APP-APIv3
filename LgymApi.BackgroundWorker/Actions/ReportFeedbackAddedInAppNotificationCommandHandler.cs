using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions.Contracts;

namespace LgymApi.BackgroundWorker.Actions;

public sealed class ReportFeedbackAddedInAppNotificationCommandHandler(
    IReportFeedbackAddedActionExecutionPort port) : IBackgroundAction<ReportFeedbackAddedInAppNotificationCommand>
{
    public Task ExecuteAsync(ReportFeedbackAddedInAppNotificationCommand command, CancellationToken cancellationToken = default) =>
        port.ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationToken);
}
