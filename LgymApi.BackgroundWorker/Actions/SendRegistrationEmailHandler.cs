using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker.Common;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using System.Text.Json;

namespace LgymApi.BackgroundWorker.Actions;

/// <summary>
/// Background action handler that schedules welcome email notifications after user registration.
/// Triggered when a new user successfully completes registration.
/// </summary>
public sealed partial class SendRegistrationEmailHandler : global::LgymApi.BackgroundWorker.Actions.Contracts.IBackgroundAction<UserRegisteredCommand>
{
    private readonly IUserRegisteredActionExecutionPort _executionPort;

    public SendRegistrationEmailHandler(
        IUserRegisteredActionExecutionPort executionPort)
    {
        _executionPort = executionPort ?? throw new ArgumentNullException(nameof(executionPort));
    }

    public async Task ExecuteAsync(UserRegisteredCommand command, CancellationToken cancellationToken = default)
    {
        await _executionPort.ExecuteAsync(
            JsonSerializer.Serialize(command, SharedSerializationOptions.Current),
            cancellationToken);
    }
}
