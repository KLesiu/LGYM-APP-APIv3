using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.BackgroundCommands;
using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.Extensions.Logging;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Registration;

internal sealed class UserRegistrationService : IUserRegistrationService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ILegacyPasswordService _legacyPasswordService;
    private readonly ICommandDispatcher _commandDispatcher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UserRegistrationService> _logger;
    private readonly AppDefaultsOptions _appDefaultsOptions;
    private readonly ITutorialService _tutorialService;

    public UserRegistrationService(UserRegistrationServiceDependencies dependencies)
    {
        _userRepository = dependencies.UserRepository;
        _roleRepository = dependencies.RoleRepository;
        _legacyPasswordService = dependencies.LegacyPasswordService;
        _commandDispatcher = dependencies.CommandDispatcher;
        _unitOfWork = dependencies.UnitOfWork;
        _logger = dependencies.Logger;
        _appDefaultsOptions = dependencies.AppDefaultsOptions;
        _tutorialService = dependencies.TutorialService;
    }

    public Task<Result<Id<UserEntity>, AppError>> RegisterAsync(
        RegisterUserInput input,
        CancellationToken cancellationToken = default)
    {
        return RegisterCoreAsync(input, [AuthConstants.Roles.User], cancellationToken);
    }

    public Task<Result<Id<UserEntity>, AppError>> RegisterTrainerAsync(
        RegisterUserInput input,
        CancellationToken cancellationToken = default)
    {
        var trainerInput = input with { IsVisibleInRanking = false, PreferredLanguage = null };
        return RegisterCoreAsync(trainerInput, [AuthConstants.Roles.User, AuthConstants.Roles.Trainer], cancellationToken);
    }

    private async Task<Result<Id<UserEntity>, AppError>> RegisterCoreAsync(
        RegisterUserInput input,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return Result<Id<UserEntity>, AppError>.Failure(new InvalidUserError(Messages.NameIsRequired));
        }

        var normalizedEmail = input.Email?.Trim().ToLowerInvariant();
        if (!new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            return Result<Id<UserEntity>, AppError>.Failure(new InvalidUserError(Messages.EmailInvalid));
        }

        if (input.Password.Length < 6)
        {
            return Result<Id<UserEntity>, AppError>.Failure(new InvalidUserError(Messages.PasswordMin));
        }

        if (!string.Equals(input.Password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result<Id<UserEntity>, AppError>.Failure(new InvalidUserError(Messages.SamePassword));
        }

        var existingUser = await _userRepository.FindByNameOrEmailAsync(input.Name, normalizedEmail!, cancellationToken);
        if (existingUser != null)
        {
            return string.Equals(existingUser.Name, input.Name, StringComparison.Ordinal)
                ? Result<Id<UserEntity>, AppError>.Failure(new ConflictError(Messages.UserWithThatName))
                : Result<Id<UserEntity>, AppError>.Failure(new ConflictError(Messages.UserWithThatEmail));
        }

        var passwordData = _legacyPasswordService.Create(input.Password);
        var user = new UserEntity
        {
            Id = Id<UserEntity>.New(),
            Name = input.Name,
            Email = normalizedEmail!,
            IsVisibleInRanking = input.IsVisibleInRanking ?? true,
            ProfileRank = "Junior 1",
            LegacyHash = passwordData.Hash,
            LegacySalt = passwordData.Salt,
            LegacyIterations = passwordData.Iterations,
            LegacyKeyLength = passwordData.KeyLength,
            LegacyDigest = passwordData.Digest,
            PreferredLanguage = ResolvePreferredLanguage(input.PreferredLanguage),
            PreferredTimeZone = _appDefaultsOptions.PreferredTimeZone
        };

        await _userRepository.AddAsync(user, cancellationToken);

        var rolesToAssign = await _roleRepository.GetByNamesAsync(roleNames, cancellationToken);
        if (rolesToAssign.Count != roleNames.Count)
        {
            return Result<Id<UserEntity>, AppError>.Failure(new InternalServerError(Messages.DefaultRoleMissing));
        }

        await _roleRepository.AddUserRolesAsync(user.Id, rolesToAssign.Select(role => role.Id).ToList(), cancellationToken);
        await _commandDispatcher.EnqueueAsync(new UserRegisteredCommand { UserId = user.Id });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            await _tutorialService.InitializeOnboardingTutorialAsync(user.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to initialize onboarding tutorial for user {UserId}. Registration is still successful.",
                user.Id);
        }

        return Result<Id<UserEntity>, AppError>.Success(user.Id);
    }

    private string ResolvePreferredLanguage(string? preferredLanguageHeader)
    {
        if (string.IsNullOrWhiteSpace(preferredLanguageHeader))
        {
            return _appDefaultsOptions.PreferredLanguage;
        }

        var candidate = preferredLanguageHeader
            .Split(',')
            .Select(part => part.Split(';').FirstOrDefault()?.Trim())
            .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return _appDefaultsOptions.PreferredLanguage;
        }

        try
        {
            return CultureInfo.GetCultureInfo(candidate).Name;
        }
        catch (CultureNotFoundException)
        {
            return _appDefaultsOptions.PreferredLanguage;
        }
    }
}
