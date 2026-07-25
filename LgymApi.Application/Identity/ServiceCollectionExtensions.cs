using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Identity.Authentication;
using LgymApi.Application.Identity.Adapters;
using LgymApi.Application.Identity.Contracts.Authentication;
using LgymApi.Application.Identity.Contracts.Administration;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Identity.Contracts.Accounts;
using LgymApi.Application.Identity.Contracts.Profile;
using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Identity.Contracts.Sessions;
using LgymApi.Application.Identity.Administration;
using LgymApi.Application.Identity.Access;
using LgymApi.Application.Identity.Registration;
using LgymApi.Application.Identity.Profile;
using LgymApi.Application.Identity.Ranking;
using LgymApi.Application.Identity.Sessions;
using LgymApi.Application.Features.AdminManagement;
using LgymApi.Application.Features.PasswordReset;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Features.Role;
using LgymApi.Application.Notifications;
using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Identity;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<PasswordResetServiceDependencies>();
        services.AddScoped<IPasswordResetTokenGenerationService, PasswordResetTokenGenerationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IAccountLinkingService, AccountLinkingService>();
        services.AddScoped<UserCredentialLoginServiceDependencies>();
        services.AddScoped<IUserCredentialLoginService, UserCredentialLoginService>();
        services.AddScoped<UserRegistrationServiceDependencies>();
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<UserSessionTerminationServiceDependencies>(serviceProvider => new UserSessionTerminationServiceDependencies(
            serviceProvider.GetRequiredService<IUserSessionStore>(),
            (sessionId, cancellationToken) => serviceProvider
                .GetRequiredService<IPushInstallationSessionDisassociationService>()
                .StageDisassociateForSessionAsync(sessionId, cancellationToken),
            serviceProvider.GetRequiredService<IUnitOfWork>()));
        services.AddScoped<IUserSessionTerminationService, UserSessionTerminationService>();
        services.AddScoped<UserProfileServiceDependencies>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IUserRankingService, UserRankingService>();
        services.AddScoped<IRankingAccountProfileReadService, RankingAccountProfileReadService>();
        services.AddScoped<IUserAdminAccessService, UserAdminAccessService>();
        services.AddScoped<IAppConfigAuthorizationPort, AppConfigAuthorizationAdapter>();
        services.AddScoped<IUserAccessReadService, UserAccessReadService>();
        services.AddScoped<IAccountReadService, AccountReadService>();
        services.AddScoped<IUserRoleAdministrationService, UserRoleAdministrationService>();
        services.AddScoped<IExternalAuthService, ExternalAuthService>();
        services.AddScoped<IGoogleUserRegistrar, GoogleUserRegistrar>();
        services.AddScoped<ILoginResultBuilder, LoginResultBuilder>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ITutorialService, TutorialService>();

        return services;
    }
}
