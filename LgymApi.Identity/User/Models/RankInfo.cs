namespace LgymApi.Application.Features.User.Models;

public sealed class RankInfo
{
    public string Name { get; init; } = string.Empty;
    public int NeedElo { get; init; }
}
