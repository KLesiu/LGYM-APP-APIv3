using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.MainRecords.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.ValueObjects;

namespace LgymApi.Application.Features.MainRecords;

public interface IMainRecordsService
{
    Task<Result<Unit, AppError>> AddNewRecordAsync(AddMainRecordInput input, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordsHistoryAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<Result<List<MainRecordBestReadModel>, AppError>> GetLastMainRecordsAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<LgymApi.Domain.Entities.MainRecord> recordId, CancellationToken cancellationToken = default);
    Task<Result<Unit, AppError>> UpdateMainRecordAsync(UpdateMainRecordInput input, CancellationToken cancellationToken = default);
    Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordInExerciseAsync(Id<LgymApi.Domain.Entities.User> userId, Id<LgymApi.Domain.Entities.Exercise> exerciseId, CancellationToken cancellationToken = default);
}
