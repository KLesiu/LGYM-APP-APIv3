using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class TrainerInvitationCreatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalJsonAndCancellation()
    {
        var command = new TrainerInvitationCreatedInAppNotificationCommand
        {
            InvitationId = Id<TrainerInvitation>.New(),
            TrainerId = Id<User>.New(),
            TraineeId = Id<User>.New()
        };
        var preparationPort = Substitute.For<ITrainerInvitationCreatedInAppPreparationPort>();
        var deliveryPort = Substitute.For<ITrainerInvitationCreatedInAppDeliveryPort>();
        preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new TrainerInvitationCreatedInAppPreparation(
                command.InvitationId.ToString(), command.TrainerId.ToString(), command.TraineeId.ToString(),
                "trainee@example.com", "invite-code", DateTimeOffset.UtcNow, "Coach", "coach@example.com", "pl-PL", "Europe/Warsaw"));
        var handler = new TrainerInvitationCreatedInAppNotificationCommandHandler(preparationPort, deliveryPort);
        using var cancellation = new CancellationTokenSource();

        await handler.ExecuteAsync(command, cancellation.Token);

        await preparationPort.Received(1).PrepareAsync(
            Arg.Is<string>(payload => IsEquivalentCommand(payload, command)),
            cancellation.Token);
        await deliveryPort.Received(1).DeliverAsync(
            Arg.Is<TrainerInvitationCreatedInAppDeliveryRequest>(request =>
                request.InvitationId == command.InvitationId.ToString()
                && request.TrainerId == command.TrainerId.ToString()
                && request.TraineeId == command.TraineeId.ToString()
                && request.InvitationCode == "invite-code"),
            cancellation.Token);
    }

    private static bool IsEquivalentCommand(string payload, TrainerInvitationCreatedInAppNotificationCommand command)
    {
        var deserialized = JsonSerializer.Deserialize<TrainerInvitationCreatedInAppNotificationCommand>(payload, SharedSerializationOptions.Current);
        return deserialized is not null
            && deserialized.InvitationId == command.InvitationId
            && deserialized.TrainerId == command.TrainerId
            && deserialized.TraineeId == command.TraineeId;
    }
}
