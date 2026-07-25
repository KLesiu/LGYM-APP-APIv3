using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Contracts.Ranking;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Identity.Ranking;

public sealed class UserRankingService : IUserRankingService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UserRankingService(IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Unit, AppError>> ChangeVisibilityInRankingAsync(
        UserEntity? currentUser,
        bool isVisibleInRanking,
        CancellationToken cancellationToken = default)
    {
        if (currentUser == null)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.DidntFind));
        }

        currentUser.IsVisibleInRanking = isVisibleInRanking;
        await _userRepository.UpdateAsync(currentUser, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
