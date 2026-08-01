using LgymApi.Domain.Services;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Services;

public interface IRankService
{
    IReadOnlyList<RankDefinition> GetRanks();
    RankDefinition GetCurrentRank(Elo elo);
    RankDefinition? GetNextRank(string currentRankName);
}

internal sealed class RankService : IRankService
{
    public IReadOnlyList<RankDefinition> GetRanks() => RankDefinitions.All;

    public RankDefinition GetCurrentRank(Elo elo)
    {
        return RankDefinitions.All.Last(rank => elo >= rank.NeedElo);
    }

    public RankDefinition? GetNextRank(string currentRankName)
    {
        var ranks = RankDefinitions.All;

        for (var i = 0; i < ranks.Count; i++)
        {
            if (ranks[i].Name == currentRankName)
            {
                return i + 1 < ranks.Count ? ranks[i + 1] : null;
            }
        }

        return null;
    }
}
