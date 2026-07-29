using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Application.WorkoutProgress.Ranking.Models;
using LgymApi.Resources;

namespace LgymApi.Application.WorkoutProgress.Ranking;

public sealed class WorkoutProgressRankingReadService : IWorkoutProgressRankingReadService
{
    private readonly IRankingAccountProfileReadService _accountProfiles;
    private readonly IWorkoutEloPersistence _eloRegistryRepository;

    public WorkoutProgressRankingReadService(
        IRankingAccountProfileReadService accountProfiles,
        IWorkoutEloPersistence eloRegistryRepository)
    {
        _accountProfiles = accountProfiles;
        _eloRegistryRepository = eloRegistryRepository;
    }

    public async Task<Result<List<RankingReadModel>, AppError>> GetUsersRankingAsync(CancellationToken cancellationToken = default)
    {
        var accountProfiles = await _accountProfiles.GetRankingEligibleAccountProfilesAsync(cancellationToken);
        var ranking = new List<RankingReadModel>();

        foreach (var profile in accountProfiles)
        {
            var elo = await _eloRegistryRepository.GetLatestEloAsync(profile.Id, cancellationToken) ?? 1000;
            ranking.Add(new RankingReadModel(profile.Name, profile.Avatar, elo, profile.ProfileRank));
        }

        if (ranking.Count == 0)
        {
            return Result<List<RankingReadModel>, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        return Result<List<RankingReadModel>, AppError>.Success(ranking.OrderByDescending(entry => entry.Elo).ToList());
    }
}
