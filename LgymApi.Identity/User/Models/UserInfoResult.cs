namespace LgymApi.Application.Features.User.Models;

public sealed class UserInfoResult
{
    public string Name { get; init; } = string.Empty;
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.User> Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Avatar { get; init; }
    public string ProfileRank { get; init; } = string.Empty;
    public string PreferredTimeZone { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int Elo { get; set; }
    public RankInfo? NextRank { get; init; }
    public bool IsDeleted { get; init; }
    public bool IsVisibleInRanking { get; init; }
    public List<string> Roles { get; init; } = new();
    public List<string> PermissionClaims { get; init; } = new();
    public bool HasActiveTutorials { get; init; }
}
