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
public sealed class TraineeNoteUpdatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalJsonAndScalarFacts()
    {
        var command = new TraineeNoteUpdatedInAppNotificationCommand
        {
            TraineeNoteId = Id<TraineeNote>.New(),
            TraineeId = Id<User>.New(),
            TrainerId = Id<User>.New(),
            NoteTitle = "   ",
            TriggeredAt = new DateTimeOffset(2026, 6, 26, 0, 30, 0, TimeSpan.Zero)
        };
        var preparationPort = Substitute.For<ITraineeNoteUpdatedInAppPreparationPort>();
        var deliveryPort = Substitute.For<ITraineeNoteUpdatedInAppDeliveryPort>();
        preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new TraineeNoteUpdatedInAppPreparation(
                command.TraineeNoteId.ToString(), command.TraineeId.ToString(), command.TrainerId.ToString(),
                command.NoteTitle, command.TriggeredAt, null, null, null, null,
                "Trainee", "trainee@example.com", "pl-PL", "Europe/Warsaw"));
        var handler = new TraineeNoteUpdatedInAppNotificationCommandHandler(preparationPort, deliveryPort);
        using var cancellation = new CancellationTokenSource();

        await handler.ExecuteAsync(command, cancellation.Token);

        await preparationPort.Received(1).PrepareAsync(
            Arg.Is<string>(payload => IsEquivalentCommand(payload, command)),
            cancellation.Token);
        await deliveryPort.Received(1).DeliverAsync(
            Arg.Is<TraineeNoteUpdatedInAppDeliveryRequest>(request =>
                request.TraineeNoteId == command.TraineeNoteId.ToString()
                && request.TraineeId == command.TraineeId.ToString()
                && request.TrainerId == command.TrainerId.ToString()
                && request.NoteTitle == command.NoteTitle
                && request.TriggeredAt == command.TriggeredAt),
            cancellation.Token);
    }

    private static bool IsEquivalentCommand(string payload, TraineeNoteUpdatedInAppNotificationCommand command)
    {
        var deserialized = JsonSerializer.Deserialize<TraineeNoteUpdatedInAppNotificationCommand>(payload, SharedSerializationOptions.Current);
        return deserialized is not null
            && deserialized.TraineeNoteId == command.TraineeNoteId
            && deserialized.TraineeId == command.TraineeId
            && deserialized.TrainerId == command.TrainerId
            && deserialized.NoteTitle == command.NoteTitle
            && deserialized.TriggeredAt == command.TriggeredAt;
    }
}
