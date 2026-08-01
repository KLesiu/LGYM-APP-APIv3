namespace LgymApi.Application.Features.Role.Models;

public sealed class RoleResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Role> Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public List<string> PermissionClaims { get; init; } = new();
}

public sealed class PermissionClaimLookupResult
{
    public string ClaimType { get; init; } = string.Empty;
    public string ClaimValue { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
