using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Features.Role.Models;
using LgymApi.Application.Features.Tutorial.Models;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.ExternalAuth;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Identity.ApiAdapters;

public sealed class IdentityApiAdapterMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<RankInfo, AccountRankProjection>((source, _) => new AccountRankProjection(source.Name, source.NeedElo));
        configuration.CreateMap<AccountRankProjection, RankInfo>((source, _) => new RankInfo
        {
            Name = source.Name,
            NeedElo = source.NeedElo
        });
        configuration.CreateMap<UserInfoResult, AccountProfileProjection>((source, context) => new AccountProfileProjection(
            source.Id.Rebind<AccountReference>(),
            source.Name,
            source.Email,
            source.Avatar,
            source.ProfileRank,
            source.PreferredTimeZone,
            source.CreatedAt,
            source.UpdatedAt,
            source.Elo,
            source.NextRank is null ? null : context!.Map<RankInfo, AccountRankProjection>(source.NextRank),
            source.IsDeleted,
            source.IsVisibleInRanking,
            source.Roles,
            source.PermissionClaims,
            source.HasActiveTutorials));
        configuration.CreateMap<AccountProfileProjection, UserInfoResult>((source, context) => new UserInfoResult
        {
            Id = source.Id.Rebind<User>(),
            Name = source.Name,
            Email = source.Email,
            Avatar = source.Avatar,
            ProfileRank = source.ProfileRank,
            PreferredTimeZone = source.PreferredTimeZone,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            Elo = source.Elo,
            NextRank = source.NextRank is null ? null : context!.Map<AccountRankProjection, RankInfo>(source.NextRank),
            IsDeleted = source.IsDeleted,
            IsVisibleInRanking = source.IsVisibleInRanking,
            Roles = source.Roles.ToList(),
            PermissionClaims = source.PermissionClaims.ToList(),
            HasActiveTutorials = source.HasActiveTutorials
        });
        configuration.CreateMap<ExternalLoginInfo, ExternalLoginProjection>((source, _) => new ExternalLoginProjection(source.Provider, source.ProviderEmail));
        configuration.CreateMap<TutorialProgressResult, TutorialProgressProjection>((source, _) => new TutorialProgressProjection(
            source.Id.ToString(),
            source.TutorialType,
            source.TutorialName,
            source.TutorialDescription,
            source.IsCompleted,
            source.CompletedAt,
            source.CompletedSteps,
            source.RemainingSteps,
            source.TotalSteps,
            source.CompletedStepsCount));
        configuration.CreateMap<UserResult, AdminAccountProjection>((source, _) => new AdminAccountProjection(
            source.Id.Rebind<AccountReference>(),
            source.Name,
            source.Email,
            source.Avatar,
            source.ProfileRank,
            source.IsVisibleInRanking,
            source.IsBlocked,
            source.IsDeleted,
            source.CreatedAt,
            source.UpdatedAt,
            source.Roles));
        configuration.CreateMap<RoleResult, RoleProjection>((source, _) => new RoleProjection(
            source.Id.Rebind<RoleReference>(),
            source.Name,
            source.Description,
            source.PermissionClaims));
        configuration.CreateMap<PermissionClaimLookupResult, PermissionClaimProjection>((source, _) => new PermissionClaimProjection(
            source.ClaimType,
            source.ClaimValue,
            source.DisplayName));
    }
}
