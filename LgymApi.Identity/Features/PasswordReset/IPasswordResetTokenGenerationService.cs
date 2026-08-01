namespace LgymApi.Application.Features.PasswordReset;

internal interface IPasswordResetTokenGenerationService
{
    Task<GeneratedPasswordResetToken> GenerateUniqueAsync(CancellationToken cancellationToken = default);
}
