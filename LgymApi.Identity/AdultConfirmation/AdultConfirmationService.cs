using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Repositories;
using LgymApi.Application.Services;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.Extensions.Options;

namespace LgymApi.Identity.Contracts.AdultConfirmation;

internal sealed class AdultConfirmationService : IAdultConfirmationService
{
    private readonly IUserRepository _userRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AgeGateOptions _options;

    public AdultConfirmationService(
        IUserRepository userRepository,
        IUnitOfWork unitOfWork,
        IOptions<AgeGateOptions> options)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
        _options = options.Value;
    }

    public async Task<Result<Unit, AppError>> ConfirmAsync(
        Id<AccountReference> accountId,
        bool adultConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!adultConfirmed)
        {
            return Result<Unit, AppError>.Failure(new InvalidUserError(Messages.AdultConfirmationRequired));
        }

        var user = accountId.IsEmpty
            ? null
            : await _userRepository.FindByIdAsync(accountId.Rebind<User>(), cancellationToken);
        if (user is null)
        {
            return Result<Unit, AppError>.Failure(new UserNotFoundError(Messages.DidntFind));
        }

        if (user.AdultConfirmedAt is not null)
        {
            return Result<Unit, AppError>.Success(Unit.Value);
        }

        user.AdultConfirmedAt = DateTimeOffset.UtcNow;
        user.AdultConfirmationVersion = _options.ConfirmationVersion;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }
}
