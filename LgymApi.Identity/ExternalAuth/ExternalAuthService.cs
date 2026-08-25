using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Security;
using LgymApi.Resources;

namespace LgymApi.Application.ExternalAuth;

internal sealed class ExternalAuthService : IExternalAuthService
{
    private readonly IGoogleTokenValidator _googleTokenValidator;
    private readonly IGoogleUserRegistrar _googleUserRegistrar;
    private readonly ILoginResultBuilder _loginResultBuilder;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ExternalAuthService(
        IGoogleTokenValidator googleTokenValidator,
        IUserExternalLoginRepository userExternalLoginRepository,
        IGoogleUserRegistrar googleUserRegistrar,
        ILoginResultBuilder loginResultBuilder,
        IUnitOfWork unitOfWork)
    {
        _googleTokenValidator = googleTokenValidator;
        _userExternalLoginRepository = userExternalLoginRepository;
        _googleUserRegistrar = googleUserRegistrar;
        _loginResultBuilder = loginResultBuilder;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LoginResult, AppError>> GoogleSignInAsync(string idToken, string? accessToken, CancellationToken cancellationToken)
        => await GoogleSignInAsync(idToken, accessToken, adultConfirmed: false, cancellationToken);

    public async Task<Result<LoginResult, AppError>> GoogleSignInAsync(
        string idToken,
        string? accessToken,
        bool adultConfirmed,
        CancellationToken cancellationToken)
    {
        var payload = await _googleTokenValidator.ValidateAsync(idToken, accessToken, cancellationToken);
        if (payload == null)
        {
            return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.GoogleTokenInvalid));
        }

        if (!payload.EmailVerified)
        {
            return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.GoogleEmailNotVerified));
        }

        var existingExternalLogin = await _userExternalLoginRepository.FindByProviderAsync(
            AuthConstants.ExternalProviders.Google,
            payload.Subject,
            cancellationToken);

        if (existingExternalLogin != null)
        {
            var existingUser = existingExternalLogin.User;
            if (existingUser == null || existingUser.IsDeleted)
            {
                return Result<LoginResult, AppError>.Failure(new UserUnauthorizedError(Messages.Unauthorized));
            }

            if (existingUser.IsBlocked)
            {
                return Result<LoginResult, AppError>.Failure(new ForbiddenError(Messages.AccountBlocked));
            }

            var existingResult = await _loginResultBuilder.BuildAsync(existingUser, existingUser.PreferredTimeZone, cancellationToken);
            if (existingResult.IsSuccess)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return existingResult;
        }

        if (!adultConfirmed)
        {
            return Result<LoginResult, AppError>.Failure(new AdultConfirmationRequiredForRegistrationError());
        }

        var createdUserResult = await _googleUserRegistrar.RegisterAsync(payload, cancellationToken);
        if (!createdUserResult.IsSuccess)
        {
            return Result<LoginResult, AppError>.Failure(createdUserResult.Error!);
        }

        var createdResult = await _loginResultBuilder.BuildAsync(createdUserResult.Value!, createdUserResult.Value!.PreferredTimeZone, cancellationToken);
        if (createdResult.IsSuccess)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return createdResult;
    }
}
