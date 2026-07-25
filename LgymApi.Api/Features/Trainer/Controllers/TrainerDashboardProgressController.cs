using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.EloRegistry.Contracts;
using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Api.Features.Training.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.Progress.EloChart;
using LgymApi.Application.Coaching.Progress.ExerciseScoresChart;
using LgymApi.Application.Coaching.Progress.MainRecordsHistory;
using LgymApi.Application.Coaching.Progress.TrainingByDate;
using LgymApi.Application.Coaching.Progress.TrainingDates;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.Coaching.Relationships.UnlinkTrainee;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerDashboardProgressController : ControllerBase
{
    private readonly IGetTrainerDashboardUseCase _getDashboard;
    private readonly IGetTrainingDatesUseCase _getTrainingDates;
    private readonly IGetTrainingByDateUseCase _getTrainingByDate;
    private readonly IGetExerciseScoresChartUseCase _getExerciseScoresChart;
    private readonly IGetEloChartUseCase _getEloChart;
    private readonly IGetMainRecordsHistoryUseCase _getMainRecordsHistory;
    private readonly IUnlinkTraineeUseCase _unlinkTrainee;
    private readonly IMapper _mapper;

    public TrainerDashboardProgressController(
        IGetTrainerDashboardUseCase getDashboard,
        IGetTrainingDatesUseCase getTrainingDates,
        IGetTrainingByDateUseCase getTrainingByDate,
        IGetExerciseScoresChartUseCase getExerciseScoresChart,
        IGetEloChartUseCase getEloChart,
        IGetMainRecordsHistoryUseCase getMainRecordsHistory,
        IUnlinkTraineeUseCase unlinkTrainee,
        IMapper mapper)
    {
        _getDashboard = getDashboard;
        _getTrainingDates = getTrainingDates;
        _getTrainingByDate = getTrainingByDate;
        _getExerciseScoresChart = getExerciseScoresChart;
        _getEloChart = getEloChart;
        _getMainRecordsHistory = getMainRecordsHistory;
        _unlinkTrainee = unlinkTrainee;
        _mapper = mapper;
    }

    [HttpGet("trainees")]
    [ProducesResponseType(typeof(TrainerDashboardTraineesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardTrainees([FromQuery] TrainerDashboardTraineesRequest request, CancellationToken cancellationToken = default)
    {
        var trainer = HttpContext.GetCurrentUser();
        var query = _mapper.Map<TrainerDashboardTraineesRequest, GetTrainerDashboardQuery>(request) with { TrainerId = trainer!.Id };
        var result = await _getDashboard.ExecuteAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(new TrainerDashboardTraineesResponse
        {
            Page = result.Value.Page,
            PageSize = result.Value.PageSize,
            Total = result.Value.TotalCount,
            Items = _mapper.MapList<TrainerDashboardTraineeReadModel, TrainerDashboardTraineeDto>(result.Value.Items)
        });
    }

    [HttpGet("trainees/{traineeId}/trainings/dates")]
    [ProducesResponseType(typeof(List<DateTime>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeTrainingDates([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<DateTime>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getTrainingDates.ExecuteAsync(new GetTrainingDatesQuery(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(result.Value);
    }

    [HttpPost("trainees/{traineeId}/trainings/by-date")]
    [ProducesResponseType(typeof(List<TrainingByDateDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeTrainingByDate([FromRoute] string traineeId, [FromBody] TrainingByDateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var query = _mapper.Map<TrainingByDateRequestDto, GetTrainingByDateQuery>(request) with { TrainerId = trainer!.Id, TraineeId = parsedTraineeId };
        var result = await _getTrainingByDate.ExecuteAsync(query, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<WorkoutProgressDashboardTrainingReadModel, TrainingByDateDetailsDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/exercise-scores/chart")]
    [ProducesResponseType(typeof(List<ExerciseScoresChartDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeExerciseScoresChartData([FromRoute] string traineeId, [FromBody] ExerciseScoresChartRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<ExerciseEntity>.TryParse(request.ExerciseId, out var parsedExerciseId))
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.ExerciseIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var query = _mapper.Map<ExerciseScoresChartRequestDto, GetExerciseScoresChartQuery>(request) with
        {
            TrainerId = trainer!.Id,
            TraineeId = parsedTraineeId,
            ExerciseId = parsedExerciseId
        };
        var result = await _getExerciseScoresChart.ExecuteAsync(query, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<ExerciseScoreChartPoint, ExerciseScoresChartDataDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/elo/chart")]
    [ProducesResponseType(typeof(List<EloRegistryBaseChartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeEloChart([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<EloChartPoint>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getEloChart.ExecuteAsync(new GetEloChartQuery(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<EloChartPoint, EloRegistryBaseChartDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/main-records/history")]
    [ProducesResponseType(typeof(List<MainRecordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeMainRecordsHistory([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<MainRecordReadModel>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getMainRecordsHistory.ExecuteAsync(new GetMainRecordsHistoryQuery(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<MainRecordReadModel, MainRecordResponseDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/unlink")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlinkTrainee([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _unlinkTrainee.ExecuteAsync(new UnlinkTraineeCommand(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
