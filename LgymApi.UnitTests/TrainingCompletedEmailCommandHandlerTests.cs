using System.Text.Json;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundActions;
using LgymApi.Application.WorkoutProgress.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainingCompletedEmailCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalPayloadAndPreparedDelivery()
    {
        var command = new TrainingCompletedCommand { UserId = Id<User>.New(), TrainingId = Id<Training>.New() };
        var preparation = Substitute.For<ITrainingCompletedEmailPreparationPort>();
        var delivery = Substitute.For<ITrainingCompletedEmailDeliveryPort>();
        preparation.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new TrainingCompletedEmailPreparation(
            command.UserId.ToString(), command.TrainingId.ToString(), "athlete@example.test", "en-US", "UTC", "Plan", DateTimeOffset.UtcNow, []));
        await new TrainingCompletedEmailCommandHandler(preparation, delivery).ExecuteAsync(command);
        await preparation.Received(1).PrepareAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), Arg.Any<CancellationToken>());
        await delivery.Received(1).DeliverAsync(Arg.Any<TrainingCompletedEmailDeliveryRequest>(), Arg.Any<CancellationToken>());
    }
}
