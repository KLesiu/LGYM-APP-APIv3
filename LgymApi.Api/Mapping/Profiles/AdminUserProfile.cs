using LgymApi.Api.Features.AdminManagement.Contracts;
using LgymApi.Application.Features.AdminManagement.Models;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class AdminUserProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<UserResult, AdminUserDto>((source, _) => new AdminUserDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Email = source.Email,
            Avatar = source.Avatar,
            ProfileRank = source.ProfileRank,
            IsVisibleInRanking = source.IsVisibleInRanking,
            IsBlocked = source.IsBlocked,
            IsDeleted = source.IsDeleted,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            Roles = source.Roles
        });

        configuration.CreateMap<AdminAccountProjection, AdminUserDto>((source, _) => new AdminUserDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Email = source.Email,
            Avatar = source.Avatar,
            ProfileRank = source.ProfileRank,
            IsVisibleInRanking = source.IsVisibleInRanking,
            IsBlocked = source.IsBlocked,
            IsDeleted = source.IsDeleted,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            Roles = source.Roles.ToList()
        });
    }
}
