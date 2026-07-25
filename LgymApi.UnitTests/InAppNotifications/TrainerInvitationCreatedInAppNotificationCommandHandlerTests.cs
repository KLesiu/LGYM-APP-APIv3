using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
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
public sealed class TrainerInvitationCreatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_WhenInvitationExists_SubmitsTheExactInAppIntent()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitationId = Id<TrainerInvitation>.New();
        var command = new TrainerInvitationCreatedInAppNotificationCommand
        {
            InvitationId = invitationId,
            TrainerId = trainerId,
            TraineeId = traineeId,
        };
        var invitation = new CoachingInvitationNotificationFact(
            invitationId,
            trainerId,
            traineeId,
            "trainee@example.com",
            "invite-code",
            DateTimeOffset.UtcNow.AddDays(1));
        var trainer = Account(trainerId, "Coach", "pl-PL");
        var reads = Substitute.For<ICoachingNotificationReadService>();
        var accounts = Substitute.For<IAccountReadService>();
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        CoachingNotificationIntent? submittedIntent = null;
        reads.GetInvitationAsync(invitationId, Arg.Any<CancellationToken>()).Returns(invitation);
        accounts.GetByIdAsync(trainerId, Arg.Any<CancellationToken>()).Returns(trainer);
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submittedIntent = call.Arg<CoachingNotificationIntent>();
                return Task.FromResult(new CoachingNotificationIntentResult(null, null));
            });
        var handler = new TrainerInvitationCreatedInAppNotificationCommandHandler(
            intents,
            reads,
            accounts,
            Substitute.For<ILogger<TrainerInvitationCreatedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(command);

        await reads.Received(1).GetInvitationAsync(invitationId, Arg.Any<CancellationToken>());
        await accounts.Received(1).GetByIdAsync(trainerId, Arg.Any<CancellationToken>());
        submittedIntent.Should().BeEquivalentTo(new InvitationCreatedCoachingNotificationIntent(
            CoachingNotificationLegacyChannel.InApp,
            invitationId,
            trainerId,
            traineeId,
            invitation.InviteeEmail,
            invitation.InvitationCode,
            invitation.ExpiresAt,
            trainer,
            null));
    }

    [Test]
    public async Task ExecuteAsync_WhenInvitationFactIsMissing_DoesNotSubmitAnIntent()
    {
        var reads = Substitute.For<ICoachingNotificationReadService>();
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        var invitationId = Id<TrainerInvitation>.New();
        CoachingNotificationIntent? submittedIntent = null;
        reads.GetInvitationAsync(invitationId, Arg.Any<CancellationToken>()).Returns((CoachingInvitationNotificationFact?)null);
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                submittedIntent = call.Arg<CoachingNotificationIntent>();
                return Task.FromResult(new CoachingNotificationIntentResult(null, null));
            });
        var handler = new TrainerInvitationCreatedInAppNotificationCommandHandler(
            intents,
            reads,
            Substitute.For<IAccountReadService>(),
            Substitute.For<ILogger<TrainerInvitationCreatedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(new TrainerInvitationCreatedInAppNotificationCommand
        {
            InvitationId = invitationId,
            TrainerId = Id<User>.New(),
            TraineeId = Id<User>.New(),
        });

        submittedIntent.Should().BeNull();
    }

    [Test]
    public async Task ExecuteAsync_WhenInAppDeliveryFails_LogsAnError()
    {
        var trainerId = Id<User>.New();
        var traineeId = Id<User>.New();
        var invitationId = Id<TrainerInvitation>.New();
        var reads = Substitute.For<ICoachingNotificationReadService>();
        var intents = Substitute.For<ICoachingNotificationIntentService>();
        var logger = Substitute.For<ILogger<TrainerInvitationCreatedInAppNotificationCommandHandler>>();
        reads.GetInvitationAsync(invitationId, Arg.Any<CancellationToken>()).Returns(new CoachingInvitationNotificationFact(
            invitationId, trainerId, traineeId, "trainee@example.com", "invite-code", DateTimeOffset.UtcNow));
        intents.SubmitAsync(Arg.Any<CoachingNotificationIntent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CoachingNotificationIntentResult(null, new BadRequestError("boom"))));
        var handler = new TrainerInvitationCreatedInAppNotificationCommandHandler(
            intents,
            reads,
            Substitute.For<IAccountReadService>(),
            logger);

        await handler.ExecuteAsync(new TrainerInvitationCreatedInAppNotificationCommand
        {
            InvitationId = invitationId,
            TrainerId = trainerId,
            TraineeId = traineeId,
        });

        ErrorLogCount(logger).Should().Be(1);
    }

    private static AccountReadModel Account(Id<User> id, string name, string culture)
        => new(id, name, "person@example.com", null, culture, "Europe/Warsaw");

    private static int ErrorLogCount<THandler>(ILogger<THandler> logger)
        => logger.ReceivedCalls().Count(call => call.GetArguments()[0] is LogLevel.Error);
}
