using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Notifications;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Notifications.InApp;
using LgymApi.Application.Options;
using LgymApi.Identity.Contracts.Accounts;
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

    [Test]
    public async Task ExecuteAsync_CreatesLocalizedTrainerToTraineeNotification()
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var submissionId = Id<ReportSubmission>.New();
        var writer = Substitute.For<IInAppNotificationWireWriter>();
        var accounts = Substitute.For<IAccountLookupService>();
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>())
            .Returns(ReportingTestData.Lookup(trainerId, "Trener Jan"));
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>())
            .Returns(ReportingTestData.Lookup(traineeId, "Podopieczny", "pl-PL"));
        var port = new ReportFeedbackAddedActionExecutionPort(
            writer,
            accounts,
            new AppDefaultsOptions { PreferredLanguage = "en-US" });
        var command = new ReportFeedbackAddedInAppNotificationCommand
        {
            SubmissionId = submissionId,
            TraineeId = traineeId,
            TrainerId = trainerId,
            TemplateName = "Tydzień 1",
            TriggeredAt = DateTimeOffset.UtcNow
        };

        await port.ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current));

        await writer.Received(1).CreateAsync(
            traineeId.ToString(),
            trainerId.ToString(),
            Arg.Any<string>(),
            "Trener Jan dodał komentarz do Twojego raportu: Tydzień 1.",
            $"/trainer/report-submissions/{submissionId}",
            "ReportFeedbackReceived",
            Arg.Any<CancellationToken>());
    }
}
