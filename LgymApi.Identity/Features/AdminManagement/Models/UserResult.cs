using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.AdminManagement.Models;

public sealed class UserResult
{
    public Id<Domain.Entities.User> Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Avatar { get; init; }
    public string ProfileRank { get; init; } = string.Empty;
    public bool IsVisibleInRanking { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public List<string> Roles { get; init; } = new();
}
