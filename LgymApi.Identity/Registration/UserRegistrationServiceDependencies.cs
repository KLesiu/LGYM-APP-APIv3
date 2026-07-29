using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Options;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Identity.Registration;

internal sealed class UserRegistrationServiceDependencies
{
    public UserRegistrationServiceDependencies(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILegacyPasswordService legacyPasswordService,
        ICommandDispatcher commandDispatcher,
        IUnitOfWork unitOfWork,
        ILogger<UserRegistrationService> logger,
        AppDefaultsOptions appDefaultsOptions,
        ITutorialService tutorialService)
    {
        UserRepository = userRepository;
        RoleRepository = roleRepository;
        LegacyPasswordService = legacyPasswordService;
        CommandDispatcher = commandDispatcher;
        UnitOfWork = unitOfWork;
        Logger = logger;
        AppDefaultsOptions = appDefaultsOptions;
        TutorialService = tutorialService;
    }

    public IUserRepository UserRepository { get; }
    public IRoleRepository RoleRepository { get; }
    public ILegacyPasswordService LegacyPasswordService { get; }
    public ICommandDispatcher CommandDispatcher { get; }
    public IUnitOfWork UnitOfWork { get; }
    public ILogger<UserRegistrationService> Logger { get; }
    public AppDefaultsOptions AppDefaultsOptions { get; }
    public ITutorialService TutorialService { get; }
}
