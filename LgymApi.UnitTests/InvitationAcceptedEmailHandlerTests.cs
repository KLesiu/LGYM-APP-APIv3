using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InvitationAcceptedEmailHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalPayloadAndPreparedDelivery()
    {
        var command = new InvitationAcceptedCommand { InvitationId = Id<TrainerInvitation>.New() };
        var preparation = Substitute.For<IInvitationAcceptedEmailPreparationPort>();
        var delivery = Substitute.For<IInvitationAcceptedEmailDeliveryPort>();
        preparation.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new InvitationAcceptedEmailPreparation(
            command.InvitationId.ToString(), Id<User>.New().ToString(), Id<User>.New().ToString(), "coach@example.test", "en-US", "UTC", "Coach", "Trainee"));

        await new InvitationAcceptedEmailHandler(preparation, delivery).ExecuteAsync(command);

        await preparation.Received(1).PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), Arg.Any<CancellationToken>());
        await delivery.Received(1).DeliverAsync(Arg.Any<InvitationAcceptedEmailDeliveryRequest>(), Arg.Any<CancellationToken>());
    }
}
