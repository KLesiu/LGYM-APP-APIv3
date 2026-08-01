using System.Text.Json;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Notifications.Contracts.Email
{
    public interface IUserRegisteredActionExecutionPort
    {
        Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default);
    }
}

namespace LgymApi.Application.Notifications.Email
{
internal sealed class UserRegisteredActionExecutionPort(
    IRegistrationWelcomeEmailPreparationPort preparationPort,
    IWelcomeEmailDeliveryPort deliveryPort,
    ILogger<UserRegisteredActionExecutionPort> logger) : IUserRegisteredActionExecutionPort
{
    public async Task ExecuteAsync(string payloadJson, CancellationToken cancellationToken = default)
    {
        var command = JsonSerializer.Deserialize<UserRegisteredCommand>(payloadJson, SharedSerializationOptions.Current)
            ?? throw new InvalidOperationException("User registration action payload is invalid.");
        var preparation = await preparationPort.PrepareAsync(command.UserId.ToString(), cancellationToken);
        if (preparation is null) return;

        await deliveryPort.DeliverAsync(
            new WelcomeEmailDeliveryRequest(preparation.UserId, preparation.UserName, preparation.RecipientEmail, preparation.CultureName),
            cancellationToken);
        logger.LogInformation("Welcome email scheduled for User {UserId} to {Email}", command.UserId, preparation.RecipientEmail);
    }
}
}
