using System.Security.Cryptography;
using System.Text;
using LgymApi.Application.Repositories;

namespace LgymApi.Application.Features.PasswordReset;

/// <summary>
/// Generates cryptographically secure password reset tokens with guaranteed uniqueness.
/// Uses collision-checking loop to ensure token hash uniqueness before insertion.
/// </summary>
internal sealed class PasswordResetTokenGenerationService : IPasswordResetTokenGenerationService
{
    private const int TokenBytesLength = 32;
    private const int MaxRetries = 10;

    private readonly IPasswordResetTokenRepository _repository;

    public PasswordResetTokenGenerationService(IPasswordResetTokenRepository repository)
    {
        _repository = repository;
    }

    public async Task<GeneratedPasswordResetToken> GenerateUniqueAsync(CancellationToken cancellationToken = default)
    {
        // Retry loop ensures uniqueness: if hash collision occurs (extremely unlikely),
        // regenerate and check again
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            var plainTextToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenBytesLength));
            var tokenHash = ComputeSha256Hex(plainTextToken);

            // Check if this hash already exists
            var hashExists = await _repository.TokenHashExistsAsync(tokenHash, cancellationToken);
            if (!hashExists)
            {
                // Hash is unique, return it
                return new GeneratedPasswordResetToken(plainTextToken, tokenHash);
            }

            // Hash collision (extremely rare), loop and try again
        }

        // Should never reach here (collision probability is negligible)
        throw new InvalidOperationException("Failed to generate unique password reset token after maximum retries.");
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
