using System.Text.Json;
using FluentAssertions;
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
public sealed class TrainerRelationshipEndedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_SerializesCanonicalCommandAndForwardsPreparedScalars()
    {
        var command = new TrainerRelationshipEndedInAppNotificationCommand { TrainerId = Id<User>.New(), TraineeId = Id<User>.New() };
        var preparation = Substitute.For<IRelationshipEndedPreparationPort>();
        var delivery = Substitute.For<IRelationshipEndedDeliveryPort>();
        preparation.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new RelationshipEndedPreparation(
            command.TrainerId.ToString(), command.TraineeId.ToString(), "Coach", "coach@example.test", "pl-PL", "Europe/Warsaw", null, null, null, null));

        await new TrainerRelationshipEndedInAppNotificationCommandHandler(preparation, delivery).ExecuteAsync(command);

        await preparation.Received(1).PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), Arg.Any<CancellationToken>());
        await delivery.Received(1).DeliverAsync(Arg.Is<RelationshipEndedDeliveryRequest>(request =>
            request.TrainerId == command.TrainerId.ToString() && request.TraineeId == command.TraineeId.ToString() && request.TrainerName == "Coach"), Arg.Any<CancellationToken>());
    }
}
