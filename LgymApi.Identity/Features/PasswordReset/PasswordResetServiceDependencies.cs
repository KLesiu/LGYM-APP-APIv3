using LgymApi.Application.Features.PasswordReset.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;

namespace LgymApi.Application.Features.PasswordReset;

internal sealed class PasswordResetServiceDependencies
{
    public PasswordResetServiceDependencies(
        IUserRepository userRepository,
        IPasswordResetTokenRepository passwordResetTokenRepository,
        IPasswordResetTokenGenerationService tokenGenerationService,
        ILegacyPasswordService legacyPasswordService,
        IPasswordRecoveryEmailScheduler passwordRecoveryEmailScheduler,
        IUserSessionStore userSessionStore,
        IUnitOfWork unitOfWork)
    {
        UserRepository = userRepository;
        PasswordResetTokenRepository = passwordResetTokenRepository;
        TokenGenerationService = tokenGenerationService;
        LegacyPasswordService = legacyPasswordService;
        PasswordRecoveryEmailScheduler = passwordRecoveryEmailScheduler;
        UserSessionStore = userSessionStore;
        UnitOfWork = unitOfWork;
    }

    public IUserRepository UserRepository { get; }
    public IPasswordResetTokenRepository PasswordResetTokenRepository { get; }
    public IPasswordResetTokenGenerationService TokenGenerationService { get; }
    public ILegacyPasswordService LegacyPasswordService { get; }
    public IPasswordRecoveryEmailScheduler PasswordRecoveryEmailScheduler { get; }
    public IUserSessionStore UserSessionStore { get; }
    public IUnitOfWork UnitOfWork { get; }
}
