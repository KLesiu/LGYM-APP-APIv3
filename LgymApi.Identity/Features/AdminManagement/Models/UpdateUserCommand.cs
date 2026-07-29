namespace LgymApi.Application.Features.AdminManagement.Models;

public sealed class UpdateUserCommand
{
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string ProfileRank { get; init; } = string.Empty;
    public bool IsVisibleInRanking { get; init; }
    public string? Avatar { get; init; }
}
