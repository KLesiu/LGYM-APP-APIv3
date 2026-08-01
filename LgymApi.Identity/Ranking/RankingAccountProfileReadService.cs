using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Repositories;

namespace LgymApi.Application.Identity.Ranking;

internal sealed class RankingAccountProfileReadService : IRankingAccountProfileReadService
{
    private readonly IUserRepository _userRepository;

    public RankingAccountProfileReadService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public Task<List<RankingAccountProfile>> GetRankingEligibleAccountProfilesAsync(CancellationToken cancellationToken = default)
        => _userRepository.GetRankingEligibleAccountProfilesAsync(cancellationToken);
}
