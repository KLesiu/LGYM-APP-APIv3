using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class TrainerInvitationRejectedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_SubmitsTheExactInAppIntent()
    {
        var command = new TrainerInvitationRejectedInAppNotificationCommand
        {
            InvitationId = Id<TrainerInvitation>.New(),
            TrainerId = Id<User>.New(),
            TraineeId = Id<User>.New(),
        };
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        CoachingNotificationIntent? submittedIntent = null;
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submittedIntent = call.Arg<CoachingNotificationIntent>();
                return Task.FromResult(new CoachingNotificationIntentResult(null, null));
            });
        var handler = new TrainerInvitationRejectedInAppNotificationCommandHandler(
            intents,
            Substitute.For<ILogger<TrainerInvitationRejectedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(command);

        submittedIntent.Should().BeEquivalentTo(new InvitationRejectedCoachingNotificationIntent(
            CoachingNotificationLegacyChannel.InApp,
            command.InvitationId,
            command.TrainerId,
            command.TraineeId));
    }

    [Test]
    public async Task ExecuteAsync_WhenInAppDeliveryFails_LogsAnError()
    {
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        var logger = Substitute.For<ILogger<TrainerInvitationRejectedInAppNotificationCommandHandler>>();
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CoachingNotificationIntentResult(null, new BadRequestError("boom"))));
        var handler = new TrainerInvitationRejectedInAppNotificationCommandHandler(intents, logger);

        await handler.ExecuteAsync(new TrainerInvitationRejectedInAppNotificationCommand
        {
            InvitationId = Id<TrainerInvitation>.New(),
            TrainerId = Id<User>.New(),
            TraineeId = Id<User>.New(),
        });

        ErrorLogCount(logger).Should().Be(1);
    }

    private static int ErrorLogCount<THandler>(ILogger<THandler> logger)
        => logger.ReceivedCalls().Count(call => call.GetArguments()[0] is LogLevel.Error);
}
