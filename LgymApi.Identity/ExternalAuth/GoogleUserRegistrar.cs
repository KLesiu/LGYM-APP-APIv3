using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.ExternalAuth;

internal sealed class GoogleUserRegistrar : IGoogleUserRegistrar
{
    private const string DefaultProfileRank = "Junior 1";

    private readonly AppDefaultsOptions _appDefaultsOptions;
    private readonly IUserExternalLoginRepository _userExternalLoginRepository;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITutorialService _tutorialService;
    private readonly ILogger<GoogleUserRegistrar> _logger;

    public GoogleUserRegistrar(
        IUserRepository userRepository,
        IUserExternalLoginRepository userExternalLoginRepository,
        IRoleRepository roleRepository,
        IUnitOfWork unitOfWork,
        AppDefaultsOptions appDefaultsOptions,
        ITutorialService tutorialService,
        ILogger<GoogleUserRegistrar> logger)
    {
        _userRepository = userRepository;
        _userExternalLoginRepository = userExternalLoginRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
        _appDefaultsOptions = appDefaultsOptions;
        _tutorialService = tutorialService;
        _logger = logger;
    }

    public async Task<Result<User, AppError>> RegisterAsync(GoogleTokenPayload payload, CancellationToken cancellationToken)
    {
        var normalizedEmail = new Email(payload.Email);
        var localUser = await _userRepository.FindByEmailAsync(normalizedEmail, cancellationToken);
        if (localUser != null)
        {
            return Result<User, AppError>.Failure(new ConflictError(Messages.GoogleEmailConflict));
        }

        var userRole = await _roleRepository.GetByNamesAsync([AuthConstants.Roles.User], cancellationToken);
        if (userRole.Count != 1)
        {
            return Result<User, AppError>.Failure(new InternalServerError(Messages.DefaultRoleMissing));
        }

        var user = new User
        {
            Id = Id<User>.New(),
            Name = await GenerateUniqueUserNameAsync(normalizedEmail.Value, cancellationToken),
            Email = normalizedEmail,
            IsVisibleInRanking = true,
            ProfileRank = DefaultProfileRank,
            PreferredLanguage = _appDefaultsOptions.PreferredLanguage,
            PreferredTimeZone = _appDefaultsOptions.PreferredTimeZone
        };

        var externalLogin = new UserExternalLogin
        {
            Id = Id<UserExternalLogin>.New(),
            UserId = user.Id,
            Provider = AuthConstants.ExternalProviders.Google,
            ProviderKey = payload.Subject,
            ProviderEmail = normalizedEmail.Value
        };

        await _userRepository.AddAsync(user, cancellationToken);
        await _userExternalLoginRepository.AddAsync(externalLogin, cancellationToken);
        await _roleRepository.AddUserRolesAsync(user.Id, [userRole[0].Id], cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.GetType().Name == "DbUpdateException")
        {
            return Result<User, AppError>.Failure(new ConflictError(Messages.GoogleEmailConflict));
        }

        try
        {
            await _tutorialService.InitializeOnboardingTutorialAsync(user.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to initialize onboarding tutorial for Google user {UserId}. Registration is still successful.",
                user.Id);
        }

        var createdUser = await _userRepository.FindByIdWithRolesAsync(user.Id, cancellationToken);
        return createdUser == null
            ? Result<User, AppError>.Failure(new InternalServerError(Messages.UserLoadFailed))
            : Result<User, AppError>.Success(createdUser);
    }

    private async Task<string> GenerateUniqueUserNameAsync(string email, CancellationToken cancellationToken)
    {
        var atIndex = email.IndexOf('@');
        var baseName = atIndex > 0 ? email[..atIndex] : email;
        var candidateBase = string.Concat(baseName.Where(char.IsLetterOrDigit));

        if (string.IsNullOrWhiteSpace(candidateBase))
        {
            candidateBase = "user";
        }

        var candidate = candidateBase;
        var suffix = 1;

        while (await _userRepository.FindByNameAsync(candidate, cancellationToken) != null)
        {
            suffix++;
            candidate = $"{candidateBase}{suffix}";
        }

        return candidate;
    }
}
