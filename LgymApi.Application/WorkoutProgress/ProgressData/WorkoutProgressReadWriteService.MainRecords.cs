using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Application.WorkoutProgress.Persistence;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using MainRecordEntity = LgymApi.Domain.Entities.MainRecord;

namespace LgymApi.Application.WorkoutProgress.ProgressData;

public sealed partial class WorkoutProgressReadWriteService
{
    public async Task<Result<Unit, AppError>> AddMainRecordAsync(MainRecordCreateWriteModel input, CancellationToken cancellationToken = default)
    {
        if (input.UserId.IsEmpty || input.ExerciseId.IsEmpty) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        var exercise = await _dependencies.ExerciseRepository.FindByIdAsync(input.ExerciseId, cancellationToken);
        if (await _dependencies.AccountAccess.GetByIdAsync(input.UserId, cancellationToken) is null || exercise == null) return Result<Unit, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        if (input.Unit == WeightUnits.Unknown) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.FieldRequired));
        await _dependencies.MainRecordRepository.AddAsync(new WorkoutMainRecordWriteModel(Id<MainRecordEntity>.New(), input.UserId, exercise.Id, input.Weight, input.Unit, new DateTimeOffset(DateTime.SpecifyKind(input.Date, DateTimeKind.Utc))), cancellationToken);
        await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordHistoryAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty) return Result<List<MainRecordReadModel>, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        if (await _dependencies.AccountAccess.GetByIdAsync(userId, cancellationToken) is null) return Result<List<MainRecordReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        var records = await _dependencies.MainRecordRepository.GetByAccountIdAsync(userId, cancellationToken);
        return records.Count == 0 ? Result<List<MainRecordReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind)) : Result<List<MainRecordReadModel>, AppError>.Success(records.OrderBy(record => record.Date).Select(MapMainRecord).ToList());
    }

    public async Task<Result<List<MainRecordBestReadModel>, AppError>> GetBestMainRecordsAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty) return Result<List<MainRecordBestReadModel>, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        if (await _dependencies.AccountAccess.GetByIdAsync(userId, cancellationToken) is null) return Result<List<MainRecordBestReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        var records = (await _dependencies.MainRecordRepository.GetBestByAccountGroupedByExerciseAndUnitAsync(userId, null, cancellationToken)).Where(record => record.Unit != WeightUnits.Unknown).GroupBy(record => record.ExerciseId).Select(group => GetBestRecord(group.ToList())).ToList();
        if (records.Count == 0) return Result<List<MainRecordBestReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        var exercises = await _dependencies.ExerciseRepository.GetByIdsAsync(records.Select(record => record.ExerciseId).Distinct().ToList(), cancellationToken);
        var map = exercises.ToDictionary(exercise => exercise.Id);
        return Result<List<MainRecordBestReadModel>, AppError>.Success(records.Where(record => map.ContainsKey(record.ExerciseId)).Select(record => new MainRecordBestReadModel(MapMainRecord(record), MapExercise(map[record.ExerciseId]))).ToList());
    }

    public async Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<LgymApi.Identity.Contracts.AccountReference> currentUserId, Id<MainRecordEntity> recordId, CancellationToken cancellationToken = default)
    {
        if (currentUserId.IsEmpty || recordId.IsEmpty) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        var record = await _dependencies.MainRecordRepository.FindByIdAsync(recordId, cancellationToken);
        if (record == null) return Result<Unit, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        if (record.AccountId != currentUserId) return Result<Unit, AppError>.Failure(new MainRecordsForbiddenError(Messages.Forbidden));
        await _dependencies.MainRecordRepository.DeleteAsync(record.Id, cancellationToken); await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken); return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UpdateMainRecordAsync(MainRecordUpdateWriteModel input, CancellationToken cancellationToken = default)
    {
        if (input.RouteUserId.IsEmpty || input.CurrentUserId.IsEmpty || input.RecordId.IsEmpty || input.ExerciseId.IsEmpty) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        if (input.RouteUserId != input.CurrentUserId) return Result<Unit, AppError>.Failure(new MainRecordsForbiddenError(Messages.Forbidden));
        var record = await _dependencies.MainRecordRepository.FindByIdAsync(input.RecordId, cancellationToken); var exercise = await _dependencies.ExerciseRepository.FindByIdAsync(input.ExerciseId, cancellationToken);
        if (record == null || exercise == null) return Result<Unit, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        if (record.AccountId != input.CurrentUserId) return Result<Unit, AppError>.Failure(new MainRecordsForbiddenError(Messages.Forbidden));
        if (input.Unit == WeightUnits.Unknown) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.FieldRequired));
        var updated = new WorkoutMainRecordWriteModel(record.Id, record.AccountId, exercise.Id, input.Weight, input.Unit, new DateTimeOffset(DateTime.SpecifyKind(input.Date, DateTimeKind.Utc)));
        await _dependencies.MainRecordRepository.UpdateAsync(updated, cancellationToken); await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken); return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordAsync(Id<LgymApi.Identity.Contracts.AccountReference> userId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty || exerciseId.IsEmpty) return Result<PossibleRecordReadModel, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        var records = await _dependencies.MainRecordRepository.GetBestByAccountGroupedByExerciseAndUnitAsync(userId, [exerciseId], cancellationToken);
        var comparable = records.Where(record => record.Unit != WeightUnits.Unknown).ToList();
        if (comparable.Count > 0) { var best = GetBestRecord(comparable); return Result<PossibleRecordReadModel, AppError>.Success(new PossibleRecordReadModel(best.Weight, 1, best.Unit, best.Date.UtcDateTime)); }
        var possible = await _dependencies.ExerciseScoreRepository.GetBestScoreAsync(userId, exerciseId, cancellationToken);
        return possible == null ? Result<PossibleRecordReadModel, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind)) : Result<PossibleRecordReadModel, AppError>.Success(new PossibleRecordReadModel(possible.Weight, possible.Reps, possible.Unit, possible.CreatedAt.UtcDateTime));
    }

    private WorkoutMainRecordPersistenceModel GetBestRecord(List<WorkoutMainRecordPersistenceModel> records) => records.Aggregate((best, candidate) => CompareWeights(candidate.Weight, candidate.Unit, best.Weight, best.Unit) > 0 || (CompareWeights(candidate.Weight, candidate.Unit, best.Weight, best.Unit) == 0 && candidate.Date > best.Date) ? candidate : best);
    private int CompareWeights(double leftValue, WeightUnits leftUnit, double rightValue, WeightUnits rightUnit) => UnitValueComparer.Compare(leftValue, leftUnit, rightValue, rightUnit, (value, unit) => _dependencies.WeightUnitConverter.Convert(value, unit, WeightUnits.Kilograms));
    private static MainRecordReadModel MapMainRecord(WorkoutMainRecordPersistenceModel record) => new(record.Id, record.ExerciseId, record.Weight, record.Unit, record.Date.UtcDateTime);
    private static ProgressExerciseReadModel MapExercise(WorkoutExercisePersistenceModel exercise) => new(exercise.Id, exercise.Name, exercise.OwnerId, exercise.BodyPart, exercise.EloFormula, exercise.Description, exercise.Image);
}
