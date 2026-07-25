using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
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
        if (!await _dependencies.UserAccess.UserExistsAsync(input.UserId, cancellationToken) || exercise == null) return Result<Unit, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        if (input.Unit == WeightUnits.Unknown) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.FieldRequired));
        await _dependencies.MainRecordRepository.AddAsync(new MainRecordEntity { Id = Id<MainRecordEntity>.New(), UserId = input.UserId, ExerciseId = exercise.Id, Weight = new Weight(input.Weight, input.Unit), Date = new DateTimeOffset(DateTime.SpecifyKind(input.Date, DateTimeKind.Utc)) }, cancellationToken);
        await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordHistoryAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty) return Result<List<MainRecordReadModel>, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        if (!await _dependencies.UserAccess.UserExistsAsync(userId, cancellationToken)) return Result<List<MainRecordReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        var records = await _dependencies.MainRecordRepository.GetByUserIdAsync(userId, cancellationToken);
        return records.Count == 0 ? Result<List<MainRecordReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind)) : Result<List<MainRecordReadModel>, AppError>.Success(records.OrderBy(record => record.Date).Select(MapMainRecord).ToList());
    }

    public async Task<Result<List<MainRecordBestReadModel>, AppError>> GetBestMainRecordsAsync(Id<LgymApi.Domain.Entities.User> userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty) return Result<List<MainRecordBestReadModel>, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        if (!await _dependencies.UserAccess.UserExistsAsync(userId, cancellationToken)) return Result<List<MainRecordBestReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        var records = (await _dependencies.MainRecordRepository.GetBestByUserGroupedByExerciseAndUnitAsync(userId, null, cancellationToken)).Where(record => record.Weight.Unit != WeightUnits.Unknown).GroupBy(record => record.ExerciseId).Select(group => GetBestRecord(group.ToList())).ToList();
        if (records.Count == 0) return Result<List<MainRecordBestReadModel>, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        var exercises = await _dependencies.ExerciseRepository.GetByIdsAsync(records.Select(record => record.ExerciseId).Distinct().ToList(), cancellationToken);
        var map = exercises.ToDictionary(exercise => exercise.Id);
        return Result<List<MainRecordBestReadModel>, AppError>.Success(records.Where(record => map.ContainsKey(record.ExerciseId)).Select(record => new MainRecordBestReadModel(MapMainRecord(record), MapExercise(map[record.ExerciseId]))).ToList());
    }

    public async Task<Result<Unit, AppError>> DeleteMainRecordAsync(Id<LgymApi.Domain.Entities.User> currentUserId, Id<MainRecordEntity> recordId, CancellationToken cancellationToken = default)
    {
        if (currentUserId.IsEmpty || recordId.IsEmpty) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        var record = await _dependencies.MainRecordRepository.FindByIdAsync(recordId, cancellationToken);
        if (record == null) return Result<Unit, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        if (record.UserId != currentUserId) return Result<Unit, AppError>.Failure(new MainRecordsForbiddenError(Messages.Forbidden));
        await _dependencies.MainRecordRepository.DeleteAsync(record, cancellationToken); await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken); return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<Unit, AppError>> UpdateMainRecordAsync(MainRecordUpdateWriteModel input, CancellationToken cancellationToken = default)
    {
        if (input.RouteUserId.IsEmpty || input.CurrentUserId.IsEmpty || input.RecordId.IsEmpty || input.ExerciseId.IsEmpty) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        if (input.RouteUserId != input.CurrentUserId) return Result<Unit, AppError>.Failure(new MainRecordsForbiddenError(Messages.Forbidden));
        var record = await _dependencies.MainRecordRepository.FindByIdAsync(input.RecordId, cancellationToken); var exercise = await _dependencies.ExerciseRepository.FindByIdAsync(input.ExerciseId, cancellationToken);
        if (record == null || exercise == null) return Result<Unit, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind));
        if (record.UserId != input.CurrentUserId) return Result<Unit, AppError>.Failure(new MainRecordsForbiddenError(Messages.Forbidden));
        if (input.Unit == WeightUnits.Unknown) return Result<Unit, AppError>.Failure(new InvalidMainRecordsError(Messages.FieldRequired));
        record.ExerciseId = exercise.Id; record.Weight = new Weight(input.Weight, input.Unit); record.Date = new DateTimeOffset(DateTime.SpecifyKind(input.Date, DateTimeKind.Utc));
        await _dependencies.MainRecordRepository.UpdateAsync(record, cancellationToken); await _dependencies.UnitOfWork.SaveChangesAsync(cancellationToken); return Result<Unit, AppError>.Success(Unit.Value);
    }

    public async Task<Result<PossibleRecordReadModel, AppError>> GetRecordOrPossibleRecordAsync(Id<LgymApi.Domain.Entities.User> userId, Id<ExerciseEntity> exerciseId, CancellationToken cancellationToken = default)
    {
        if (userId.IsEmpty || exerciseId.IsEmpty) return Result<PossibleRecordReadModel, AppError>.Failure(new InvalidMainRecordsError(Messages.InvalidId));
        var records = await _dependencies.MainRecordRepository.GetBestByUserGroupedByExerciseAndUnitAsync(userId, [exerciseId], cancellationToken);
        var comparable = records.Where(record => record.Weight.Unit != WeightUnits.Unknown).ToList();
        if (comparable.Count > 0) { var best = GetBestRecord(comparable); return Result<PossibleRecordReadModel, AppError>.Success(new PossibleRecordReadModel(best.Weight.Value, 1, best.Weight.Unit, best.Date.UtcDateTime)); }
        var possible = await _dependencies.ExerciseScoreRepository.GetBestScoreAsync(userId, exerciseId, cancellationToken);
        return possible == null ? Result<PossibleRecordReadModel, AppError>.Failure(new MainRecordsNotFoundError(Messages.DidntFind)) : Result<PossibleRecordReadModel, AppError>.Success(new PossibleRecordReadModel(possible.Weight.Value, possible.Reps, possible.Weight.Unit, possible.CreatedAt.UtcDateTime));
    }

    private MainRecordEntity GetBestRecord(List<MainRecordEntity> records) => records.Aggregate((best, candidate) => CompareWeights(candidate.Weight.Value, candidate.Weight.Unit, best.Weight.Value, best.Weight.Unit) > 0 || (CompareWeights(candidate.Weight.Value, candidate.Weight.Unit, best.Weight.Value, best.Weight.Unit) == 0 && candidate.Date > best.Date) ? candidate : best);
    private int CompareWeights(double leftValue, WeightUnits leftUnit, double rightValue, WeightUnits rightUnit) => UnitValueComparer.Compare(leftValue, leftUnit, rightValue, rightUnit, (value, unit) => _dependencies.WeightUnitConverter.Convert(value, unit, WeightUnits.Kilograms));
    private static MainRecordReadModel MapMainRecord(MainRecordEntity record) => new(record.Id, record.ExerciseId, record.Weight.Value, record.Weight.Unit, record.Date.UtcDateTime);
    private static ProgressExerciseReadModel MapExercise(ExerciseEntity exercise) => new(exercise.Id, exercise.Name, exercise.UserId, exercise.BodyPart, exercise.EloFormula, exercise.Description, exercise.Image);
}
