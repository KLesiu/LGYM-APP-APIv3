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
public sealed class TrainerInvitationRejectedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_SerializesCanonicalCommandAndForwardsScalarPreparation()
    {
        var command = new TrainerInvitationRejectedInAppNotificationCommand
        {
            InvitationId = Id<TrainerInvitation>.New(), TrainerId = Id<User>.New(), TraineeId = Id<User>.New()
        };
        var preparationPort = Substitute.For<ITrainerInvitationRejectedInAppPreparationPort>();
        var deliveryPort = Substitute.For<ITrainerInvitationRejectedInAppDeliveryPort>();
        preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new TrainerInvitationRejectedInAppPreparation(
            command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString()));
        var handler = new TrainerInvitationRejectedInAppNotificationCommandHandler(preparationPort, deliveryPort,
            Substitute.For<ILogger<TrainerInvitationRejectedInAppNotificationCommandHandler>>());

        await handler.ExecuteAsync(command);

        var serialized = JsonSerializer.Serialize(command, SharedSerializationOptions.Current);
        await preparationPort.Received(1).PrepareAsync(serialized, Arg.Any<CancellationToken>());
        await deliveryPort.Received(1).DeliverAsync(new TrainerInvitationRejectedInAppDeliveryRequest(
            command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString()), Arg.Any<CancellationToken>());
    }
}
