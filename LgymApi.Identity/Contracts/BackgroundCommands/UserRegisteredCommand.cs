using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Identity.Contracts.BackgroundCommands;

public sealed class UserRegisteredCommand : IActionCommand
{
    public Id<User> UserId { get; init; }
}
