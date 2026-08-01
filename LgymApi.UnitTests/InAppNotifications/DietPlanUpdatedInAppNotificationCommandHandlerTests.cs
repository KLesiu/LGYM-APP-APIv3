using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Notifications.Contracts.InApp;
using LgymApi.Application.Nutrition.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests.InAppNotifications;

[TestFixture]
public sealed class DietPlanUpdatedInAppNotificationCommandHandlerTests
{
    [Test]
    public async Task ExecuteAsync_SerializesCanonicalCommandAndForwardsCancellation()
    {
        var command = new DietPlanUpdatedInAppNotificationCommand
        {
            DietPlanId = Id<DietPlan>.New(), TraineeId = Id<User>.New(), TrainerId = Id<User>.New(), DietPlanName = "Strength cycle", TriggeredAt = DateTimeOffset.UtcNow
        };
        var port = Substitute.For<IDietPlanUpdatedActionExecutionPort>();
        using var cancellationSource = new CancellationTokenSource();

        await new DietPlanUpdatedInAppNotificationCommandHandler(port).ExecuteAsync(command, cancellationSource.Token);

        await port.Received(1).ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationSource.Token);
    }
}
