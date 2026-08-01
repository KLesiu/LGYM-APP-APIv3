using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class TrainerInvitationAcceptedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_SerializesCanonicalCommandAndForwardsScalarPreparation()
    {
        var command = new TrainerInvitationAcceptedInAppNotificationCommand
        {
            InvitationId = Id<TrainerInvitation>.New(), TrainerId = Id<User>.New(), TraineeId = Id<User>.New()
        };
        var preparationPort = Substitute.For<ITrainerInvitationAcceptedInAppPreparationPort>();
        var deliveryPort = Substitute.For<ITrainerInvitationAcceptedInAppDeliveryPort>();
        preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new TrainerInvitationAcceptedInAppPreparation(
            command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString()));
        var handler = new TrainerInvitationAcceptedInAppNotificationCommandHandler(preparationPort, deliveryPort,
            Substitute.For<ILogger<TrainerInvitationAcceptedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(command);

        var payload = await preparationPort.Received(1).PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        var serialized = JsonSerializer.Serialize(command, SharedSerializationOptions.Current);
        await preparationPort.Received(1).PrepareAsync(serialized, Arg.Any<CancellationToken>());
        await deliveryPort.Received(1).DeliverAsync(new TrainerInvitationAcceptedInAppDeliveryRequest(
            command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString()), Arg.Any<CancellationToken>());
    }
}
