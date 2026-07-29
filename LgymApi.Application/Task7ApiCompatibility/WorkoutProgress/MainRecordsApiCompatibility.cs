using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.MainRecords;
using LgymApi.Application.Features.MainRecords.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;

public interface IMainRecordsApiCompatibilityService
{
    Task<Result<Unit, AppError>> AddNewRecordAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, double weight, WeightUnits unit, DateTime date, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordsHistoryAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordBestReadModel>, AppError>> GetLastMainRecordsAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<AccountReference> currentAccountId, Id<LgymApi.Domain.Entities.MainRecord> recordId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateMainRecordAsync(Id<AccountReference> routeAccountId, Id<AccountReference> currentAccountId, Id<LgymApi.Domain.Entities.MainRecord> recordId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, double weight, WeightUnits unit, DateTime date, CancellationToken cancellationToken = default);
    Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordInExerciseAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
}

internal sealed class MainRecordsApiCompatibilityService : IMainRecordsApiCompatibilityService
{
    private readonly IMainRecordsService _mainRecordsService;

    public MainRecordsApiCompatibilityService(IMainRecordsService mainRecordsService)
    {
        _mainRecordsService = mainRecordsService;
    }

    public Task<Result<Unit, AppError>> AddNewRecordAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, double weight, WeightUnits unit, DateTime date, CancellationToken cancellationToken = default)
        => _mainRecordsService.AddNewRecordAsync(new AddMainRecordInput(accountId, exerciseId, weight, unit, date), cancellationToken);

    public Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordsHistoryAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _mainRecordsService.GetMainRecordsHistoryAsync(accountId, cancellationToken);

    public Task<Result<List<MainRecordBestReadModel>, AppError>> GetLastMainRecordsAsync(Id<AccountReference> accountId, CancellationToken cancellationToken = default)
        => _mainRecordsService.GetLastMainRecordsAsync(accountId, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<AccountReference> currentAccountId, Id<LgymApi.Domain.Entities.MainRecord> recordId, CancellationToken cancellationToken = default)
        => _mainRecordsService.DeleteMainRecordAsync(currentAccountId, recordId, cancellationToken);

    public Task<Result<Unit, AppError>> UpdateMainRecordAsync(Id<AccountReference> routeAccountId, Id<AccountReference> currentAccountId, Id<LgymApi.Domain.Entities.MainRecord> recordId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, double weight, WeightUnits unit, DateTime date, CancellationToken cancellationToken = default)
        => _mainRecordsService.UpdateMainRecordAsync(
            new UpdateMainRecordInput(routeAccountId, currentAccountId, recordId, exerciseId, weight, unit, date),
            cancellationToken);

    public Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordInExerciseAsync(Id<AccountReference> accountId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default)
        => _mainRecordsService.GetRecordOrPossibleRecordInExerciseAsync(accountId, exerciseId, cancellationToken);
}
