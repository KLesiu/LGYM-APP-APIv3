namespace LgymApi.Application.Identity.Contracts.Ranking;

public interface IRankingAccountProfileReadService
{
    Task<List<RankingAccountProfile>> GetRankingEligibleAccountProfilesAsync(CancellationToken cancellationToken = default);
}
