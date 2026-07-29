using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Services;

namespace LgymApi.Application.Identity.Mapping;

internal sealed class IdentityUserMappingProfile : IMappingProfile
{
    internal static class Keys
    {
        internal static readonly ContextKey<string> DefaultPreferredTimeZone = new("Identity.User.DefaultPreferredTimeZone");
        internal static readonly ContextKey<int> Elo = new("Identity.User.Elo");
        internal static readonly ContextKey<RankDefinition?> NextRank = new("Identity.User.NextRank");
        internal static readonly ContextKey<List<string>> Roles = new("Identity.User.Roles");
        internal static readonly ContextKey<List<string>> PermissionClaims = new("Identity.User.PermissionClaims");
        internal static readonly ContextKey<bool> HasActiveTutorials = new("Identity.User.HasActiveTutorials");
    }

    public void Configure(MappingConfiguration configuration)
    {
        configuration.AllowContextKey(Keys.DefaultPreferredTimeZone);
        configuration.AllowContextKey(Keys.Elo);
        configuration.AllowContextKey(Keys.NextRank);
        configuration.AllowContextKey(Keys.Roles);
        configuration.AllowContextKey(Keys.PermissionClaims);
        configuration.AllowContextKey(Keys.HasActiveTutorials);

        configuration.CreateMap<RankDefinition, RankInfo>((source, _) => new RankInfo
        {
            Name = source.Name,
            NeedElo = source.NeedElo
        });

        configuration.CreateMap<User, UserInfoResult>((source, context) =>
        {
            var nextRank = context?.Get(Keys.NextRank);

            return new UserInfoResult
            {
                Name = source.Name,
                Id = source.Id,
                Email = source.Email,
                Avatar = source.Avatar,
                ProfileRank = source.ProfileRank,
                PreferredTimeZone = string.IsNullOrWhiteSpace(source.PreferredTimeZone)
                    ? context?.Get(Keys.DefaultPreferredTimeZone) ?? string.Empty
                    : source.PreferredTimeZone,
                CreatedAt = source.CreatedAt.UtcDateTime,
                UpdatedAt = source.UpdatedAt.UtcDateTime,
                Elo = context?.Get(Keys.Elo) ?? 1000,
                NextRank = nextRank == null ? null : context!.Map<RankDefinition, RankInfo>(nextRank),
                IsDeleted = source.IsDeleted,
                IsVisibleInRanking = source.IsVisibleInRanking,
                Roles = context?.Get(Keys.Roles) ?? [],
                PermissionClaims = context?.Get(Keys.PermissionClaims) ?? [],
                HasActiveTutorials = context?.Get(Keys.HasActiveTutorials) ?? false
            };
        });

        configuration.CreateMap<UserRankingEntry, RankingEntry>((source, _) => new RankingEntry
        {
            Name = source.User.Name,
            Avatar = source.User.Avatar,
            Elo = source.Elo,
            ProfileRank = source.User.ProfileRank
        });
    }
}
