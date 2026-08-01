using System.Text.Json;
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
public sealed class UpdateTrainingMainRecordsHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalTrainingPayload()
    {
        var command = new TrainingCompletedCommand { UserId = Id<User>.New(), TrainingId = Id<Training>.New() };
        var port = Substitute.For<ITrainingMainRecordsUpdatePort>();
        await new UpdateTrainingMainRecordsHandler(port).ExecuteAsync(command);
        await port.Received(1).UpdateAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), Arg.Any<CancellationToken>());
    }
}
