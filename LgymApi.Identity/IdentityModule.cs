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
using LgymApi.Application.Platform.ReferenceData.AppConfig.Contracts;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Services;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddScoped<ILegacyPasswordService, LegacyPasswordService>();
        services.AddScoped<IUserSessionStore, UserSessionStore>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITutorialProgressRepository, TutorialProgressRepository>();
        services.AddScoped<IAdminUserService, AdminUserService>();
        services.AddScoped<IPasswordResetTokenGenerationService, PasswordResetTokenGenerationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();
        services.AddScoped<IRankService, RankService>();
        services.AddScoped<IAccountLinkingService, AccountLinkingService>();
        services.AddScoped<IUserCredentialLoginService, UserCredentialLoginService>();
        services.AddScoped<IUserRegistrationService, UserRegistrationService>();
        services.AddScoped<IRegistrationWelcomeEmailPreparationPort, RegistrationWelcomeEmailPreparationService>();
        services.AddScoped<IUserSessionTerminationService, UserSessionTerminationService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IUserRankingService, UserRankingService>();
        services.AddScoped<IRankingAccountProfileReadService, RankingAccountProfileReadService>();
        services.AddScoped<IUserAdminAccessService, UserAdminAccessService>();
        services.AddScoped<IAppConfigAuthorizationPort, AppConfigAuthorizationAdapter>();
        services.AddScoped<IUserAccessReadService, UserAccessReadService>();
        services.AddScoped<IAccountReadService, AccountReadService>();
        services.AddScoped<IAccountLookupService, AccountLookupService>();
        services.AddScoped<IAccountAccessReader, AccountAccessReader>();
        services.AddScoped<IAccountSessionValidator, AccountSessionValidator>();
        services.AddScoped<IAuthenticatedAccountContextResolver, AuthenticatedAccountContextResolver>();
        services.AddScoped<IAuthenticatedAccountCompatibilityPort, AuthenticatedAccountCompatibilityPort>();
        services.AddScoped<IUserRoleAdministrationService, UserRoleAdministrationService>();
        services.AddScoped<IExternalAuthService, ExternalAuthService>();
        services.AddScoped<IGoogleUserRegistrar, GoogleUserRegistrar>();
        services.AddScoped<ILoginResultBuilder, LoginResultBuilder>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ITutorialService, TutorialService>();

        return services;
    }
}
