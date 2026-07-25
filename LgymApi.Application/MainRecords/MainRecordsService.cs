using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.MainRecords.Models;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.MainRecords;

public sealed class MainRecordsService : IMainRecordsService
{
    private readonly IWorkoutProgressReadWriteService _progress;

    public MainRecordsService(IWorkoutProgressReadWriteService progress)
    {
        _progress = progress;
    }

    public Task<Result<Unit, AppError>> AddNewRecordAsync(AddMainRecordInput input, CancellationToken cancellationToken = default)
        => _progress.AddMainRecordAsync(new(input.UserId, input.ExerciseId, input.Weight, input.Unit, input.Date), cancellationToken);

    public Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordsHistoryAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
        => _progress.GetMainRecordHistoryAsync(userId, cancellationToken);

    public Task<Result<List<MainRecordBestReadModel>, AppError>> GetLastMainRecordsAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
        => _progress.GetBestMainRecordsAsync(userId, cancellationToken);

    public Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.MainRecord> recordId, CancellationToken cancellationToken = default)
        => _progress.DeleteMainRecordAsync(currentUserId, recordId, cancellationToken);

    public Task<Result<Unit, AppError>> UpdateMainRecordAsync(UpdateMainRecordInput input, CancellationToken cancellationToken = default)
        => _progress.UpdateMainRecordAsync(new(input.RouteUserId, input.CurrentUserId, input.RecordId, input.ExerciseId, input.Weight, input.Unit, input.Date), cancellationToken);

    public Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordInExerciseAsync(Id<LgymApi.Domain.Entities.User> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default)
        => _progress.GetRecordOrPossibleRecordAsync(userId, exerciseId, cancellationToken);
}
