using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;

namespace LgymApi.Application.Identity.Profile;

internal sealed class UserProfileServiceDependencies
{
    public UserProfileServiceDependencies(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IRankService rankService,
        IUnitOfWork unitOfWork,
        AppDefaultsOptions appDefaultsOptions,
        ITutorialService tutorialService,
        IMapper mapper)
    {
        UserRepository = userRepository;
        RoleRepository = roleRepository;
        RankService = rankService;
        UnitOfWork = unitOfWork;
        AppDefaultsOptions = appDefaultsOptions;
        TutorialService = tutorialService;
        Mapper = mapper;
    }

    public IUserRepository UserRepository { get; }
    public IRoleRepository RoleRepository { get; }
    public IRankService RankService { get; }
    public IUnitOfWork UnitOfWork { get; }
    public AppDefaultsOptions AppDefaultsOptions { get; }
    public ITutorialService TutorialService { get; }
    public IMapper Mapper { get; }
}
