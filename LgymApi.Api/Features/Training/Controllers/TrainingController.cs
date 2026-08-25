using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Training.Contracts;
using LgymApi.Api.Idempotency;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.Training;
using LgymApi.Application.Features.Training.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ApiAdapters;
using LgymApi.Api.Mapping.Profiles;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.TrainingPlanning.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Training.Controllers;

[ApiController]
[Route("api")]
public sealed class TrainingController : ControllerBase
{
    private readonly ITrainingService _trainingService;
    private readonly IMapper _mapper;
    private readonly IExerciseApiAdapter _exercises;

    public TrainingController(
        ITrainingService trainingService,
        IMapper mapper,
        IExerciseApiAdapter exercises)
    {
        _trainingService = trainingService;
        _mapper = mapper;
        _exercises = exercises;
    }

    [HttpPost("{id}/addTraining")]
    [ApiIdempotency("/api/{id}/addTraining", ApiIdempotencyScopeSource.AuthenticatedUser)]
    [ProducesResponseType(typeof(TrainingSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AddTraining([FromRoute] string id, [FromBody] TrainingFormDto form, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var gymId = form.GymId.ToIdOrEmpty<LgymApi.Domain.Entities.Gym>();
        var planDayId = form.TypePlanDayId.ToIdOrEmpty<PlanDayReference>();
        var exercises = form.Exercises.Select(exercise => new TrainingExerciseInput
        {
            ExerciseId = exercise.ExerciseId.ToIdOrEmpty<ExerciseEntity>(),
            Series = exercise.Series,
            Reps = exercise.Reps,
            Weight = exercise.Weight,
            Unit = exercise.Unit
        }).ToList();

        var input = new AddTrainingInput(gymId, planDayId, form.CreatedAt, exercises);
        var result = await _trainingService.AddTrainingAsync(accountId, input, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.Map<TrainingSummaryResult, TrainingSummaryDto>(result.Value);
        return Ok(mapped);
    }

    [HttpGet("{id}/getLastTraining")]
    [ProducesResponseType(typeof(LastTrainingInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLastTraining([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var result = await _trainingService.GetLastTrainingAsync(accountId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<LgymApi.Application.Features.Training.Models.WorkoutTrainingReadModel, LastTrainingInfoDto>(result.Value));
    }

    [HttpPost("{id}/getTrainingByDate")]
    [ProducesResponseType(typeof(List<TrainingByDateDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrainingByDate([FromRoute] string id, [FromBody] TrainingByDateRequestDto request, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var result = await _trainingService.GetTrainingByDateAsync(accountId, request.CreatedAt, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var exerciseIds = result.Value
            .SelectMany(training => training.Exercises)
            .Select(exercise => exercise.ExerciseDetails)
            .OfType<LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel>()
            .Select(exercise => exercise.Id);
        var translations = await _exercises.GetDisplayNamesAsync(
            exerciseIds,
            HttpContext.GetCulturePreferences(),
            cancellationToken);
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, translations);
        var mapped = _mapper.MapList<TrainingByDateDetails, TrainingByDateDetailsDto>(
            result.Value,
            mappingContext);
        return Ok(mapped);
    }

    [HttpGet("{id}/getTrainingDates")]
    [ProducesResponseType(typeof(List<DateTime>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTrainingDates([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var result = await _trainingService.GetTrainingDatesAsync(accountId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(result.Value);
    }

    private Id<AccountReference> ParseRouteAccountIdForCurrentAccount(string routeAccountId)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        if (currentAccount is null || currentAccount.Id.IsEmpty ||
            !Id<AccountReference>.TryParse(routeAccountId, out var parsedAccountId) ||
            parsedAccountId != currentAccount.Id)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        return parsedAccountId;
    }
}
