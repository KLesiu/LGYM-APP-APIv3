using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class ReportFeedbackAddedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalPayloadAndCancellation()
    {
        var command = new ReportFeedbackAddedInAppNotificationCommand { SubmissionId = Id<ReportSubmission>.New(), TraineeId = Id<AccountReference>.New(), TrainerId = Id<AccountReference>.New(), TemplateName = "Weekly", TriggeredAt = DateTimeOffset.UtcNow };
        var port = Substitute.For<IReportFeedbackAddedActionExecutionPort>();
        using var cancellationSource = new CancellationTokenSource();

        await new ReportFeedbackAddedInAppNotificationCommandHandler(port).ExecuteAsync(command, cancellationSource.Token);

        await port.Received(1).ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationSource.Token);
    }
}
