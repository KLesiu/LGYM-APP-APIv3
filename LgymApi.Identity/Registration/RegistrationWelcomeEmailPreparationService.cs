using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Identity.Registration;

internal sealed class RegistrationWelcomeEmailPreparationService(
    IUserRepository users,
    AppDefaultsOptions defaults,
    ILogger<RegistrationWelcomeEmailPreparationService> logger) : IRegistrationWelcomeEmailPreparationPort
{
    public async Task<WelcomeEmailPreparation?> PrepareAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User>.TryParse(userId, out var parsedUserId)) return null;
        var user = await users.FindByIdAsync(parsedUserId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Welcome email skipped for User {UserId} - user not found", userId);
            return null;
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            logger.LogWarning("Welcome email skipped for User {UserId} - no recipient email provided", userId);
            return null;
        }

        return new WelcomeEmailPreparation(user.Id.ToString(), user.Name, user.Email, string.IsNullOrWhiteSpace(user.PreferredLanguage) ? defaults.PreferredLanguage : user.PreferredLanguage);
    }
}
