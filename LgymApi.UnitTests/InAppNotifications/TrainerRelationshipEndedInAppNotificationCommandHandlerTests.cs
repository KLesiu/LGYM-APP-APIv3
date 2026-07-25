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
public sealed class TrainerRelationshipEndedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_LoadsPublicAccountsAndSubmitsTheExactInAppIntent()
    {
        var command = new TrainerRelationshipEndedInAppNotificationCommand
        {
            TrainerId = Id<User>.New(),
            TraineeId = Id<User>.New(),
        };
        var trainer = Account(command.TrainerId, "Coach", "pl-PL");
        var accounts = Substitute.For<IAccountReadService>();
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        CoachingNotificationIntent? submittedIntent = null;
        accounts.GetByIdAsync(command.TrainerId, Arg.Any<CancellationToken>()).Returns(trainer);
        accounts.GetByIdAsync(command.TraineeId, Arg.Any<CancellationToken>()).Returns((AccountReadModel?)null);
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submittedIntent = call.Arg<CoachingNotificationIntent>();
                return Task.FromResult(new CoachingNotificationIntentResult(null, null));
            });
        var handler = new TrainerRelationshipEndedInAppNotificationCommandHandler(
            intents,
            accounts,
            Substitute.For<ILogger<TrainerRelationshipEndedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(command);

        await accounts.Received(1).GetByIdAsync(command.TrainerId, Arg.Any<CancellationToken>());
        await accounts.Received(1).GetByIdAsync(command.TraineeId, Arg.Any<CancellationToken>());
        submittedIntent.Should().BeEquivalentTo(new RelationshipEndedCoachingNotificationIntent(
            CoachingNotificationLegacyChannel.InApp,
            command.TrainerId,
            command.TraineeId,
            trainer,
            null));
    }

    [Test]
    public async Task ExecuteAsync_WhenInAppDeliveryFails_LogsAnError()
    {
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        var logger = Substitute.For<ILogger<TrainerRelationshipEndedInAppNotificationCommandHandler>>();
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CoachingNotificationIntentResult(null, new BadRequestError("boom"))));
        var handler = new TrainerRelationshipEndedInAppNotificationCommandHandler(
            intents,
            Substitute.For<IAccountReadService>(),
            logger);

        await handler.ExecuteAsync(new TrainerRelationshipEndedInAppNotificationCommand
        {
            TrainerId = Id<User>.New(),
            TraineeId = Id<User>.New(),
        });

        ErrorLogCount(logger).Should().Be(1);
    }

    private static AccountReadModel Account(Id<User> id, string name, string culture)
        => new(id, name, "person@example.com", null, culture, "Europe/Warsaw");

    private static int ErrorLogCount<THandler>(ILogger<THandler> logger)
        => logger.ReceivedCalls().Count(call => call.GetArguments()[0] is LogLevel.Error);
}
