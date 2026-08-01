using FluentAssertions;
using LgymApi.Application.Notifications.Contracts.Events;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Notifications;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CoachingEmailNotificationSchedulerAdapterTests
{
    [Test]
    public async Task ScheduleAsync_MapsCreatedRequestToLegacyPayloadAndForwardsFeatureState()
    {
        var feature = Substitute.For<IEmailNotificationsFeature>();
        var createdScheduler = Substitute.For<IEmailScheduler<InvitationEmailPayload>>();
        var adapter = CreateAdapter(feature, createdScheduler, Substitute.For<IEmailScheduler<InvitationAcceptedEmailPayload>>(), Substitute.For<IEmailScheduler<InvitationRevokedEmailPayload>>());
        var invitationId = Id<TrainerInvitation>.New();
        var expiresAt = new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);
        var cancellationToken = new CancellationTokenSource().Token;
        feature.Enabled.Returns(true);

        await adapter.ScheduleAsync(new CoachingEmailSchedulingRequest(
            CoachingEmailSchedulingKind.InvitationCreated,
            EmailNotificationTypes.TrainerInvitation,
            invitationId,
            invitationId.Rebind<CorrelationScope>(),
            "trainee@example.com",
            "pl-PL",
            "Europe/Madrid",
            "Coach",
            null,
            "CODE123",
            expiresAt), cancellationToken);

        adapter.Enabled.Should().BeTrue();
        await createdScheduler.Received(1).ScheduleAsync(
            Arg.Is<InvitationEmailPayload>(payload =>
                payload.InvitationId == invitationId
                && payload.InvitationCode == "CODE123"
                && payload.ExpiresAt == expiresAt
                && payload.TrainerName == "Coach"
                && payload.RecipientEmail == "trainee@example.com"
                && payload.CultureName == "pl-PL"
                && payload.PreferredTimeZone == "Europe/Madrid"),
            cancellationToken);
    }

    [Test]
    public async Task ScheduleAsync_MapsAcceptedRequestToLegacyPayload()
    {
        var acceptedScheduler = Substitute.For<IEmailScheduler<InvitationAcceptedEmailPayload>>();
        var invitationId = Id<TrainerInvitation>.New();
        var adapter = CreateAdapter(Substitute.For<IEmailNotificationsFeature>(), Substitute.For<IEmailScheduler<InvitationEmailPayload>>(), acceptedScheduler, Substitute.For<IEmailScheduler<InvitationRevokedEmailPayload>>());

        await adapter.ScheduleAsync(new CoachingEmailSchedulingRequest(
            CoachingEmailSchedulingKind.InvitationAccepted,
            EmailNotificationTypes.TrainerInvitationAccepted,
            invitationId,
            invitationId.Rebind<CorrelationScope>(),
            "coach@example.com",
            "pl-PL",
            "Europe/Warsaw",
            "Coach",
            "Trainee",
            null,
            null));

        await acceptedScheduler.Received(1).ScheduleAsync(
            Arg.Is<InvitationAcceptedEmailPayload>(payload =>
                payload.InvitationId == invitationId
                && payload.TrainerName == "Coach"
                && payload.TraineeName == "Trainee"
                && payload.RecipientEmail == "coach@example.com"
                && payload.CultureName == "pl-PL"
                && payload.PreferredTimeZone == "Europe/Warsaw"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScheduleAsync_MapsRevokedRequestToLegacyPayload()
    {
        var revokedScheduler = Substitute.For<IEmailScheduler<InvitationRevokedEmailPayload>>();
        var invitationId = Id<TrainerInvitation>.New();
        var adapter = CreateAdapter(Substitute.For<IEmailNotificationsFeature>(), Substitute.For<IEmailScheduler<InvitationEmailPayload>>(), Substitute.For<IEmailScheduler<InvitationAcceptedEmailPayload>>(), revokedScheduler);

        await adapter.ScheduleAsync(new CoachingEmailSchedulingRequest(
            CoachingEmailSchedulingKind.InvitationRevoked,
            EmailNotificationTypes.TrainerInvitationRevoked,
            invitationId,
            invitationId.Rebind<CorrelationScope>(),
            "invitee@example.com",
            "en-US",
            "Europe/Warsaw",
            "Coach",
            null,
            null,
            null));

        await revokedScheduler.Received(1).ScheduleAsync(
            Arg.Is<InvitationRevokedEmailPayload>(payload =>
                payload.InvitationId == invitationId
                && payload.TrainerName == "Coach"
                && payload.RecipientEmail == "invitee@example.com"
                && payload.CultureName == "en-US"
                && payload.PreferredTimeZone == "Europe/Warsaw"),
            Arg.Any<CancellationToken>());
    }

    private static CoachingEmailNotificationSchedulerAdapter CreateAdapter(
        IEmailNotificationsFeature feature,
        IEmailScheduler<InvitationEmailPayload> createdScheduler,
        IEmailScheduler<InvitationAcceptedEmailPayload> acceptedScheduler,
        IEmailScheduler<InvitationRevokedEmailPayload> revokedScheduler) =>
        new(feature, createdScheduler, acceptedScheduler, revokedScheduler);
}
