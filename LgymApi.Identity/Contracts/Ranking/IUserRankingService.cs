using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Contracts.Ranking;

public interface IUserRankingService
{
    Task<Result<Unit, AppError>> ChangeVisibilityInRankingAsync(
        UserEntity? currentUser,
        bool isVisibleInRanking,
        CancellationToken cancellationToken = default);
}
