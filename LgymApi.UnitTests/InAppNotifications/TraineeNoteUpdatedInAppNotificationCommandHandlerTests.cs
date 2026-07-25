using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class TraineeNoteUpdatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsPublicFactsAndUntitledNoteToTheInAppIntent()
    {
        var command = new TraineeNoteUpdatedInAppNotificationCommand
        {
            TraineeNoteId = Id<TraineeNote>.New(),
            TraineeId = Id<User>.New(),
            TrainerId = Id<User>.New(),
            NoteTitle = "   ",
            TriggeredAt = new DateTimeOffset(2026, 6, 26, 0, 30, 0, TimeSpan.Zero),
        };
        var trainee = Account(command.TraineeId, "Trainee", "pl-PL");
        var accounts = Substitute.For<IAccountReadService>();
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        CoachingNotificationIntent? submittedIntent = null;
        accounts.GetByIdAsync(command.TrainerId, Arg.Any<CancellationToken>()).Returns((AccountReadModel?)null);
        accounts.GetByIdAsync(command.TraineeId, Arg.Any<CancellationToken>()).Returns(trainee);
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submittedIntent = call.Arg<CoachingNotificationIntent>();
                return Task.FromResult(new CoachingNotificationIntentResult(null, null));
            });
        var handler = new TraineeNoteUpdatedInAppNotificationCommandHandler(
            intents,
            accounts,
            Substitute.For<ILogger<TraineeNoteUpdatedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(command);

        await accounts.Received(1).GetByIdAsync(command.TrainerId, Arg.Any<CancellationToken>());
        await accounts.Received(1).GetByIdAsync(command.TraineeId, Arg.Any<CancellationToken>());
        submittedIntent.Should().BeEquivalentTo(new TraineeNoteUpdatedCoachingNotificationIntent(
            CoachingNotificationLegacyChannel.InApp,
            command.TraineeNoteId,
            command.TraineeId,
            command.TrainerId,
            command.NoteTitle,
            command.TriggeredAt,
            null,
            trainee));
    }

    [Test]
    public async Task ExecuteAsync_WhenInAppDeliveryFails_LogsAnError()
    {
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        var logger = Substitute.For<ILogger<TraineeNoteUpdatedInAppNotificationCommandHandler>>();
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CoachingNotificationIntentResult(null, new BadRequestError("boom"))));
        var handler = new TraineeNoteUpdatedInAppNotificationCommandHandler(
            intents,
            Substitute.For<IAccountReadService>(),
            logger);

        await handler.ExecuteAsync(new TraineeNoteUpdatedInAppNotificationCommand
        {
            TraineeNoteId = Id<TraineeNote>.New(),
            TraineeId = Id<User>.New(),
            TrainerId = Id<User>.New(),
            TriggeredAt = DateTimeOffset.UtcNow,
        });

        ErrorLogCount(logger).Should().Be(1);
    }

    private static AccountReadModel Account(Id<User> id, string name, string culture)
        => new(id, name, "person@example.com", null, culture, "Europe/Warsaw");

    private static int ErrorLogCount<THandler>(ILogger<THandler> logger)
        => logger.ReceivedCalls().Count(call => call.GetArguments()[0] is LogLevel.Error);
}
