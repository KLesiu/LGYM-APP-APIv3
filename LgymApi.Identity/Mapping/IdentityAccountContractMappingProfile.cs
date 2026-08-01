using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Identity.Mapping;

internal sealed class IdentityAccountContractMappingProfile : IMappingProfile
{
    internal static class Keys
    {
        internal static readonly ContextKey<IReadOnlyList<string>> Roles = new("Identity.Account.Roles");
        internal static readonly ContextKey<IReadOnlyList<string>> PermissionClaims = new("Identity.Account.PermissionClaims");
        internal static readonly ContextKey<Id<AccountSessionReference>> SessionId = new("Identity.Account.SessionId");
    }

    public void Configure(MappingConfiguration configuration)
    {
        configuration.AllowContextKey(Keys.Roles);
        configuration.AllowContextKey(Keys.PermissionClaims);
        configuration.AllowContextKey(Keys.SessionId);

        configuration.CreateMap<User, AccountLookup>((source, _) => new AccountLookup(
            source.Id.Rebind<AccountReference>(),
            source.Name,
            source.Email,
            source.Avatar,
            source.PreferredLanguage,
            source.PreferredTimeZone,
            source.CreatedAt));

        configuration.CreateMap<User, AccountAccessFacts>((source, context) => new AccountAccessFacts(
            source.Id.Rebind<AccountReference>(),
            source.IsDeleted,
            source.IsBlocked,
            context?.Get(Keys.Roles) ?? [],
            context?.Get(Keys.PermissionClaims) ?? []));

        configuration.CreateMap<AccountAccessFacts, AuthenticatedAccountContext>((source, context) => new AuthenticatedAccountContext(
            source.Id,
            context?.Get(Keys.SessionId),
            source.Roles,
            source.PermissionClaims,
            source.IsBlocked,
            source.IsDeleted));
    }
}
