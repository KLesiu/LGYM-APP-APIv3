using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;

namespace LgymApi.Application.Identity.Authentication;

internal sealed class UserCredentialLoginServiceDependencies
{
    public UserCredentialLoginServiceDependencies(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        ILegacyPasswordService legacyPasswordService,
        IRankService rankService,
        IUserSessionStore userSessionStore,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        AppDefaultsOptions appDefaultsOptions,
        ITutorialService tutorialService,
        IMapper mapper)
    {
        UserRepository = userRepository;
        RoleRepository = roleRepository;
        LegacyPasswordService = legacyPasswordService;
        RankService = rankService;
        UserSessionStore = userSessionStore;
        TokenService = tokenService;
        UnitOfWork = unitOfWork;
        AppDefaultsOptions = appDefaultsOptions;
        TutorialService = tutorialService;
        Mapper = mapper;
    }

    public IUserRepository UserRepository { get; }
    public IRoleRepository RoleRepository { get; }
    public ILegacyPasswordService LegacyPasswordService { get; }
    public IRankService RankService { get; }
    public IUserSessionStore UserSessionStore { get; }
    public ITokenService TokenService { get; }
    public IUnitOfWork UnitOfWork { get; }
    public AppDefaultsOptions AppDefaultsOptions { get; }
    public ITutorialService TutorialService { get; }
    public IMapper Mapper { get; }
}
