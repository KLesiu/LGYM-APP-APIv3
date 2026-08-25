using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Pagination;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Application.WorkoutProgress.ProgressData;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;

namespace LgymApi.Application.Coaching.ApiAdapters;

internal sealed class TrainerDashboardProgressApiAdapter : ITrainerDashboardProgressApiPort
{
    private readonly IGetTrainerDashboardUseCase _getDashboard;
    private readonly IGetTrainingDatesUseCase _getTrainingDates;
    private readonly IGetTrainingByDateUseCase _getTrainingByDate;
    private readonly IGetExerciseScoresChartUseCase _getExerciseScoresChart;
    private readonly IGetEloChartUseCase _getEloChart;
    private readonly IGetMainRecordsHistoryUseCase _getMainRecordsHistory;
    private readonly IUnlinkTraineeUseCase _unlinkTrainee;
    private readonly IMapper _mapper;
    private readonly IWorkoutProgressReadWriteService _workoutProgress;

    public TrainerDashboardProgressApiAdapter(IGetTrainerDashboardUseCase getDashboard, IGetTrainingDatesUseCase getTrainingDates, IGetTrainingByDateUseCase getTrainingByDate, IGetExerciseScoresChartUseCase getExerciseScoresChart, IGetEloChartUseCase getEloChart, IGetMainRecordsHistoryUseCase getMainRecordsHistory, IUnlinkTraineeUseCase unlinkTrainee, IMapper mapper, IWorkoutProgressReadWriteService workoutProgress)
    {
        _getDashboard = getDashboard;
        _getTrainingDates = getTrainingDates;
        _getTrainingByDate = getTrainingByDate;
        _getExerciseScoresChart = getExerciseScoresChart;
        _getEloChart = getEloChart;
        _getMainRecordsHistory = getMainRecordsHistory;
        _unlinkTrainee = unlinkTrainee;
        _mapper = mapper;
        _workoutProgress = workoutProgress;
    }

    public Task<Result<Pagination<TrainerDashboardTraineeReadModel>, AppError>> GetDashboardAsync(AuthenticatedAccountContext trainer, string? search, string? status, string? sortBy, string? sortDirection, int page, int pageSize, CancellationToken cancellationToken = default)
        => _getDashboard.ExecuteAsync(_mapper.Map<DashboardAccountInput, GetTrainerDashboardQuery>(new(trainer.Id, search, status, sortBy, sortDirection, page, pageSize)), cancellationToken);

    public Task<Result<List<DateTime>, AppError>> GetTrainingDatesAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _getTrainingDates.ExecuteAsync(_mapper.Map<TrainerTraineeAccountInput, GetTrainingDatesQuery>(new(trainer.Id, traineeId)), cancellationToken);

    public Task<Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>> GetTrainingByDateAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, DateTime createdAt, CancellationToken cancellationToken = default)
        => _getTrainingByDate.ExecuteAsync(_mapper.Map<TrainingByDateAccountInput, GetTrainingByDateQuery>(new(trainer.Id, traineeId, createdAt)), cancellationToken);

    public Task<IReadOnlyDictionary<Id<Exercise>, string>> GetExerciseDisplayNamesAsync(IEnumerable<Id<Exercise>> exerciseIds, IReadOnlyList<string> cultures, CancellationToken cancellationToken = default)
        => _workoutProgress.GetExerciseDisplayNamesAsync(exerciseIds, cultures, cancellationToken);

    public Task<Result<List<ExerciseScoreChartPoint>, AppError>> GetExerciseScoresChartAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, Id<Exercise> exerciseId, CancellationToken cancellationToken = default)
        => _getExerciseScoresChart.ExecuteAsync(_mapper.Map<ExerciseScoresChartAccountInput, GetExerciseScoresChartQuery>(new(trainer.Id, traineeId, exerciseId)), cancellationToken);

    public Task<Result<List<EloChartPoint>, AppError>> GetEloChartAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _getEloChart.ExecuteAsync(_mapper.Map<TrainerTraineeAccountInput, GetEloChartQuery>(new(trainer.Id, traineeId)), cancellationToken);

    public Task<Result<List<MainRecordReadModel>, AppError>> GetMainRecordsHistoryAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _getMainRecordsHistory.ExecuteAsync(_mapper.Map<TrainerTraineeAccountInput, GetMainRecordsHistoryQuery>(new(trainer.Id, traineeId)), cancellationToken);

    public Task<Result<Unit, AppError>> UnlinkAsync(AuthenticatedAccountContext trainer, Id<AccountReference> traineeId, CancellationToken cancellationToken = default)
        => _unlinkTrainee.ExecuteAsync(_mapper.Map<TrainerTraineeAccountInput, UnlinkTraineeCommand>(new(trainer.Id, traineeId)), cancellationToken);
}
