using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;

namespace LgymApi.Application.ExternalAuth;

internal sealed class AccountLinkingService : IAccountLinkingService
{
    private readonly IGoogleTokenValidator _googleTokenValidator;
    private readonly IUserRepository _userRepository;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AccountLinkingService(
        IGoogleTokenValidator googleTokenValidator,
        IUserRepository userRepository,
        IUserExternalLoginRepository userExternalLoginRepository,
        IUnitOfWork unitOfWork)
    {
        _googleTokenValidator = googleTokenValidator;
        _userRepository = userRepository;
        _userExternalLoginRepository = userExternalLoginRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> LinkGoogleAsync(Id<User> userId, string idToken, string? accessToken, CancellationToken cancellationToken)
    {
        var token = await _googleTokenValidator.ValidateAsync(idToken, accessToken, cancellationToken);
        if (token == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.GoogleTokenInvalid));
        }

        if (!token.EmailVerified)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.GoogleEmailNotVerified));
        }

        var user = await _userRepository.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        var existingUserLogin = await _userExternalLoginRepository.FindByUserAndProviderAsync(
            userId,
            AuthConstants.ExternalProviders.Google,
            cancellationToken);

        if (existingUserLogin != null)
        {
            return Result<Unit, AppError>.Failure(new ConflictError(Messages.GoogleAccountAlreadyLinked));
        }

        var existingLogin = await _userExternalLoginRepository.FindByProviderAsync(
            AuthConstants.ExternalProviders.Google,
            token.Subject,
            cancellationToken);

        if (existingLogin != null)
        {
            return Result<Unit, AppError>.Failure(new ConflictError(Messages.GoogleAccountAlreadyLinked));
        }

        await _userExternalLoginRepository.AddAsync(new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = userId,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = token.Subject,
            ProviderEmail = token.Email
        }, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException")
        {
            return Result<Unit, AppError>.Failure(new ConflictError(Messages.GoogleAccountAlreadyLinked));
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UnlinkGoogleAsync(Id<User> userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        var googleLogin = await _userExternalLoginRepository.FindActiveGoogleByUserIdAsync(userId, cancellationToken);
        if (googleLogin == null)
        {
            return Result<Unit, AppError>.Failure(new NotFoundError(Messages.DidntFind));
        }

        await _userExternalLoginRepository.MarkGoogleDeletedAsync(userId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<IReadOnlyList<ExternalLoginInfo>, AppError>> GetExternalLoginsAsync(Id<User> userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Result<IReadOnlyList<ExternalLoginInfo>, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        var externalLogins = await _userExternalLoginRepository.GetByUserIdAsync(userId, cancellationToken);
        var result = externalLogins
            .OrderBy(x => x.Provider, StringComparer.Ordinal)
            .Select(x => new ExternalLoginInfo(x.Provider, x.ProviderEmail))
            .ToList()
            .AsReadOnly();

        return Result<IReadOnlyList<ExternalLoginInfo>, AppError>.Success(result);
    }
}
