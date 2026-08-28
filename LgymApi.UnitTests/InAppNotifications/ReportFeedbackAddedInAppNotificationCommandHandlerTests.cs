using System.Text.Json;
using FluentAssertions;
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
[NonParallelizable]
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

    [TestCase("traineeId")]
    [TestCase("trainerId")]
    public async Task ExecuteAsync_WithInvalidAccountId_RejectsPayload(string invalidProperty)
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["submissionId"] = Id<ReportSubmission>.New().ToString(),
            ["traineeId"] = invalidProperty == "traineeId" ? "invalid" : traineeId.ToString(),
            ["trainerId"] = invalidProperty == "trainerId" ? "invalid" : trainerId.ToString(),
            ["templateName"] = "Weekly",
            ["triggeredAt"] = DateTimeOffset.UtcNow
        });
        var port = new ReportFeedbackAddedActionExecutionPort(
            Substitute.For<IInAppNotificationWireWriter>(),
            Substitute.For<IAccountLookupService>(),
            new AppDefaultsOptions { PreferredLanguage = "en-US" });

        Func<Task> action = () => port.ExecuteAsync(payload);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*invalid {invalidProperty}*");
    }

    [TestCase("")]
    [TestCase("not-a-culture")]
    [TestCase("\0")]
    public async Task ExecuteAsync_WithMissingNamesOrCulture_UsesLocalizedFallbacks(
        string preferredLanguage)
    {
        var trainerId = Id<AccountReference>.New();
        var traineeId = Id<AccountReference>.New();
        var submissionId = Id<ReportSubmission>.New();
        var writer = Substitute.For<IInAppNotificationWireWriter>();
        var accounts = Substitute.For<IAccountLookupService>();
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>())
            .Returns(ReportingTestData.Lookup(trainerId, string.Empty));
        accounts.GetByIdAsync(traineeId, Arg.Any<CancellationToken>())
            .Returns(ReportingTestData.Lookup(traineeId, "Trainee", preferredLanguage));
        var port = new ReportFeedbackAddedActionExecutionPort(
            writer,
            accounts,
            new AppDefaultsOptions { PreferredLanguage = "pl-PL" });
        var command = new ReportFeedbackAddedInAppNotificationCommand
        {
            SubmissionId = submissionId,
            TraineeId = traineeId,
            TrainerId = trainerId,
            TemplateName = string.Empty,
            TriggeredAt = DateTimeOffset.UtcNow
        };

        await port.ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current));

        await writer.Received(1).CreateAsync(
            traineeId.ToString(),
            trainerId.ToString(),
            Arg.Any<string>(),
            "Trener dodał komentarz do Twojego raportu: raport.",
            $"/trainer/report-submissions/{submissionId}",
            "ReportFeedbackReceived",
            Arg.Any<CancellationToken>());
    }
}
