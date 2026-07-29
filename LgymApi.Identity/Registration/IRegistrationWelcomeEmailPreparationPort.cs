namespace LgymApi.Application.Identity.Contracts.Registration;

public sealed record WelcomeEmailPreparation(string UserId, string UserName, string RecipientEmail, string CultureName);

public interface IRegistrationWelcomeEmailPreparationPort
{
    Task<WelcomeEmailPreparation?> PrepareAsync(string userId, CancellationToken cancellationToken = default);
}
