using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.EloRegistry.Models;
using LgymApi.Application.Features.User.Models;
using LgymApi.Application.Identity.Contracts.Registration;
using LgymApi.Application.Repositories;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.EloRegistry;

public sealed class EloRegistryService : IEloRegistryService
{
    private readonly IWorkoutProgressReadWriteService _progress;
    private readonly IUserRegistrationService _userRegistrationService;
    private readonly IUnitOfWork _unitOfWork;

    public EloRegistryService(IWorkoutProgressReadWriteService progress, IUserRegistrationService userRegistrationService, IUnitOfWork unitOfWork)
    {
        _progress = progress;
        _userRegistrationService = userRegistrationService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<EloRegistryChartEntry>, AppError>> GetChartAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
    {
        var result = await _progress.GetEloChartAsync(userId, cancellationToken);
        return result.IsFailure
            ? Result<List<EloRegistryChartEntry>, AppError>.Failure(result.Error)
            : Result<List<EloRegistryChartEntry>, AppError>.Success(result.Value.Select(point => new EloRegistryChartEntry
            {
                Id = point.Id,
                Value = point.Value,
                Date = point.Date
            }).ToList());
    }

    public async Task<Result<Unit, AppError>> RegisterUserAsync(RegisterUserInput input, bool trainer, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);
        var registration = trainer
            ? await _userRegistrationService.RegisterTrainerAsync(input, cancellationToken)
            : await _userRegistrationService.RegisterAsync(input, cancellationToken);
        if (registration.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<Unit, AppError>.Failure(registration.Error);
        }

        var accountId = registration.Value.Rebind<LgymApi.Identity.Contracts.AccountReference>();
        await _progress.InitializeEloAsync(accountId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public Task<Result<int, AppError>> GetUserEloAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
        => _progress.GetLatestEloAsync(userId, cancellationToken);

    public async Task PopulateLatestEloAsync(UserInfoResult userInfo, CancellationToken cancellationToken = default)
        => userInfo.Elo = await _progress.GetLatestEloOrDefaultAsync(userInfo.Id.Rebind<LgymApi.Identity.Contracts.AccountReference>(), cancellationToken);

    public Task<int> GetLatestEloOrDefaultAsync(Id<LgymApi.Identity.Contracts.AccountReference> accountId, CancellationToken cancellationToken = default)
        => _progress.GetLatestEloOrDefaultAsync(accountId, cancellationToken);
}
