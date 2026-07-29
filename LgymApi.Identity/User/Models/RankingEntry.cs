namespace LgymApi.Application.Features.User.Models;

public sealed class RankingEntry
{
    public string Name { get; init; } = string.Empty;
    public string? Avatar { get; init; }
    public int Elo { get; init; }
    public string ProfileRank { get; init; } = string.Empty;
}
