using LgymApi.Api.Features.Role.Contracts;
using LgymApi.Application.Features.Role.Models;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Api.Mapping.Profiles;

public sealed class RoleProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<RoleResult, RoleDto>((source, _) => new RoleDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Description = source.Description,
            PermissionClaims = source.PermissionClaims
        });

        configuration.CreateMap<PermissionClaimLookupResult, PermissionClaimLookupDto>((source, _) => new PermissionClaimLookupDto
        {
            ClaimType = source.ClaimType,
            ClaimValue = source.ClaimValue,
            DisplayName = source.DisplayName
        });

        configuration.CreateMap<RoleProjection, RoleDto>((source, _) => new RoleDto
        {
            Id = source.Id.ToString(),
            Name = source.Name,
            Description = source.Description,
            PermissionClaims = source.PermissionClaims.ToList()
        });

        configuration.CreateMap<PermissionClaimProjection, PermissionClaimLookupDto>((source, _) => new PermissionClaimLookupDto
        {
            ClaimType = source.ClaimType,
            ClaimValue = source.ClaimValue,
            DisplayName = source.DisplayName
        });
    }
}
