using System.Text.Json;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class SendRegistrationEmailHandlerTests
{
    [Test]
    public async Task ExecuteAsync_ForwardsCanonicalRegistrationPayload()
    {
        var command = new UserRegisteredCommand { UserId = Id<User>.New() };
        var port = Substitute.For<IUserRegisteredActionExecutionPort>();
        using var cancellationSource = new CancellationTokenSource();
        await new SendRegistrationEmailHandler(port).ExecuteAsync(command, cancellationSource.Token);
        await port.Received(1).ExecuteAsync(JsonSerializer.Serialize(command, SharedSerializationOptions.Current), cancellationSource.Token);
    }
}
