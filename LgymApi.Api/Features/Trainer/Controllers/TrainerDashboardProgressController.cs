using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.EloRegistry.Contracts;
using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Api.Features.Training.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.Coaching.Relationships.TrainerDashboard;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Coaching.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.Dashboard.Models;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using LgymApi.Identity.Contracts;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerDashboardProgressController : ControllerBase
{
    private readonly ITrainerDashboardProgressApiPort _progress;
    private readonly IMapper _mapper;

    public TrainerDashboardProgressController(
        ITrainerDashboardProgressApiPort progress,
        IMapper mapper)
    {
        _progress = progress;
        _mapper = mapper;
    }

    [HttpGet("trainees")]
    [ProducesResponseType(typeof(TrainerDashboardTraineesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboardTrainees([FromQuery] TrainerDashboardTraineesRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _progress.GetDashboardAsync(HttpContext.GetAuthenticatedAccountContext()!, request.Search, request.Status, request.SortBy, request.SortDirection, request.Page, request.PageSize, cancellationToken);
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
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<DateTime>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var result = await _progress.GetTrainingDatesAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(result.Value);
    }

    [HttpPost("trainees/{traineeId}/trainings/by-date")]
    [ProducesResponseType(typeof(List<TrainingByDateDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeTrainingByDate([FromRoute] string traineeId, [FromBody] TrainingByDateRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<WorkoutProgressDashboardTrainingReadModel>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var result = await _progress.GetTrainingByDateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, request.CreatedAt, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var exerciseIds = result.Value
            .SelectMany(training => training.Exercises)
            .Select(exercise => exercise.ExerciseDetails.Id.ToIdOrEmpty<ExerciseEntity>());
        var translations = await _progress.GetExerciseDisplayNamesAsync(
            exerciseIds,
            HttpContext.GetCulturePreferences(),
            cancellationToken);
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, translations);
        return Ok(_mapper.MapList<WorkoutProgressDashboardTrainingReadModel, TrainingByDateDetailsDto>(
            result.Value,
            mappingContext));
    }

    [HttpPost("trainees/{traineeId}/exercise-scores/chart")]
    [ProducesResponseType(typeof(List<ExerciseScoresChartDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeExerciseScoresChartData([FromRoute] string traineeId, [FromBody] ExerciseScoresChartRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        if (!Id<ExerciseEntity>.TryParse(request.ExerciseId, out var parsedExerciseId))
        {
            return Result<List<ExerciseScoreChartPoint>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.ExerciseIdRequired)).ToActionResult();
        }

        var result = await _progress.GetExerciseScoresChartAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedExerciseId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<ExerciseScoreChartPoint, ExerciseScoresChartDataDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/elo/chart")]
    [ProducesResponseType(typeof(List<EloRegistryBaseChartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeEloChart([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<EloChartPoint>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var result = await _progress.GetEloChartAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<EloChartPoint, EloRegistryBaseChartDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/main-records/history")]
    [ProducesResponseType(typeof(List<MainRecordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTraineeMainRecordsHistory([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<List<MainRecordReadModel>, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var result = await _progress.GetMainRecordsHistoryAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<MainRecordReadModel, MainRecordResponseDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/unlink")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlinkTrainee([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return Result<Unit, AppError>.Failure(new InvalidTrainerRelationshipError(Messages.UserIdRequired)).ToActionResult();
        }

        var result = await _progress.UnlinkAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
