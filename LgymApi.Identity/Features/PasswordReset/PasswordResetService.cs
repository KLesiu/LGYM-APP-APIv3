using System.Security.Cryptography;
using System.Text;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

using Result = LgymApi.Application.BuildingBlocks.Results.Result<LgymApi.Application.BuildingBlocks.Results.Unit, LgymApi.Application.BuildingBlocks.Errors.AppError>;

namespace LgymApi.Application.Features.PasswordReset;

internal sealed class PasswordResetService : IPasswordResetService
{
    private const int ResetTokenExpiryMinutes = 30;

    private readonly IUserRepository _userRepository;
    private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
    private readonly IPasswordResetTokenGenerationService _tokenGenerationService;
    private readonly ILegacyPasswordService _legacyPasswordService;
    private readonly IPasswordRecoveryEmailScheduler _passwordRecoveryEmailScheduler;
    private readonly IUserSessionStore _userSessionStore;
    private readonly IUnitOfWork _unitOfWork;

    public PasswordResetService(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordResetTokenGenerationService tokenGenerationService,
        ILegacyPasswordService legacyPasswordService,
        IPasswordRecoveryEmailScheduler passwordRecoveryEmailScheduler,
        IUserSessionStore userSessionStore,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordResetTokenRepository = passwordResetTokenRepository;
        _tokenGenerationService = tokenGenerationService;
        _legacyPasswordService = legacyPasswordService;
        _passwordRecoveryEmailScheduler = passwordRecoveryEmailScheduler;
        _userSessionStore = userSessionStore;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> RequestPasswordResetAsync(string email, string cultureName, CancellationToken cancellationToken)
    {
        var emailValueObject = new Email(email);
        var user = await _userRepository.FindByEmailAsync(emailValueObject, cancellationToken);

        if (user == null || user.IsDeleted)
        {
            return Result.Success(Unit.Value);
        }

        var activeTokens = await _passwordResetTokenRepository.GetActiveForUserAsync(user.Id, cancellationToken);
        foreach (var activeToken in activeTokens)
        {
            activeToken.IsUsed = true;
            await _passwordResetTokenRepository.UpdateAsync(activeToken, cancellationToken);
        }

        var generatedToken = await _tokenGenerationService.GenerateUniqueAsync(cancellationToken);

        var resetToken = new PasswordResetToken
        {
            Id = LgymApi.Domain.ValueObjects.Id<PasswordResetToken>.New(),
            UserId = user.Id,
            TokenHash = generatedToken.TokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(ResetTokenExpiryMinutes),
            IsUsed = false
        };

        await _passwordResetTokenRepository.AddAsync(resetToken, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _passwordRecoveryEmailScheduler.ScheduleAsync(
            new PasswordRecoveryEmailRequest(
                user.Id,
                resetToken.Id,
                user.Name,
                user.Email.Value,
                generatedToken.PlainTextToken,
                ResetUrl: string.Empty,
                cultureName),
            cancellationToken);

        return Result.Success(Unit.Value);
    }

    public async Task<Result> ResetPasswordAsync(string plainTextToken, string newPassword, CancellationToken cancellationToken)
    {
        var tokenHash = ComputeSha256Hex(plainTextToken);
        var resetToken = await _passwordResetTokenRepository.FindActiveByTokenHashAsync(tokenHash, cancellationToken);
        if (resetToken == null)
        {
            return Result.Failure(new InvalidUserError(Messages.InvalidToken));
        }

        var user = await _userRepository.FindByIdAsync(resetToken.UserId, cancellationToken);
        if (user == null || user.IsDeleted)
        {
            return Result.Failure(new InvalidUserError(Messages.InvalidToken));
        }

        var passwordData = _legacyPasswordService.Create(newPassword);
        user.LegacyHash = passwordData.Hash;
        user.LegacySalt = passwordData.Salt;
        user.LegacyIterations = passwordData.Iterations;
        user.LegacyKeyLength = passwordData.KeyLength;
        user.LegacyDigest = passwordData.Digest;

        resetToken.IsUsed = true;

        await _userRepository.UpdateAsync(user, cancellationToken);
        await _passwordResetTokenRepository.UpdateAsync(resetToken, cancellationToken);
        await _userSessionStore.RevokeAllUserSessionsAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
