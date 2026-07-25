using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Extensions;
using LgymApi.Api.Features.PlanDay.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.PlanDay;
using LgymApi.Application.Features.PlanDay.Models;
using LgymApi.Application.Mapping.Core;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using PlanDayEntity = LgymApi.Domain.Entities.PlanDay;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.PlanDay.Controllers;

[ApiController]
[Route("api")]
public sealed class PlanDayController : ControllerBase
{
    private readonly IPlanDayService _planDayService;
    private readonly IMapper _mapper;

    public PlanDayController(IPlanDayService planDayService, IMapper mapper)
    {
        _planDayService = planDayService;
        _mapper = mapper;
    }

    [HttpPost("planDay/{id}/createPlanDay")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePlanDay([FromRoute] string id, [FromBody] PlanDayFormDto form, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var exercises = form.Exercises
            .Select(exercise => new PlanDayExerciseInput
            {
                ExerciseId = exercise.ExerciseId.ToIdOrEmpty<ExerciseEntity>(),
                Series = exercise.Series,
                Reps = exercise.Reps
            })
            .ToList();
        var result = await _planDayService.CreatePlanDayAsync(user!, planId, form.Name, exercises, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("planDay/updatePlanDay")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlanDay([FromBody] PlanDayFormDto form, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanDayEntity>.TryParse(form.Id ?? string.Empty, out var planDayId))
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.DidntFind)).ToActionResult();
        }

        var exercises = form.Exercises
            .Select(exercise => new PlanDayExerciseInput
            {
                ExerciseId = exercise.ExerciseId.ToIdOrEmpty<ExerciseEntity>(),
                Series = exercise.Series,
                Reps = exercise.Reps
            })
            .ToList();
        var result = await _planDayService.UpdatePlanDayAsync(user!, planDayId, form.Name, exercises, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("planDay/{id}/getPlanDay")]
    [ProducesResponseType(typeof(PlanDayVmDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDay([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanDayEntity>.TryParse(id, out var planDayId))
        {
            return Result<PlanDayDetailsContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var cultures = HttpContext.GetCulturePreferences();
        var result = await _planDayService.GetPlanDayAsync(user!, planDayId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(PlanDayProfile.Keys.PlanDayExercises, context.Exercises);
        mappingContext.Set(PlanDayProfile.Keys.ExerciseMap, context.ExerciseMap);
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var planDayVm = _mapper.Map<LgymApi.Domain.Entities.PlanDay, PlanDayVmDto>(context.PlanDay, mappingContext);
        return Ok(planDayVm);
    }

    [HttpGet("planDay/{id}/getPlanDays")]
    [ProducesResponseType(typeof(List<PlanDayVmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDays([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<PlanDaysContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var cultures = HttpContext.GetCulturePreferences();
        var result = await _planDayService.GetPlanDaysAsync(user!, planId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(PlanDayProfile.Keys.PlanDayExercises, context.PlanDayExercises);
        mappingContext.Set(PlanDayProfile.Keys.ExerciseMap, context.ExerciseMap);
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var planDays = _mapper.MapList<LgymApi.Domain.Entities.PlanDay, PlanDayVmDto>(context.PlanDays, mappingContext);
        return Ok(planDays);
    }

    [HttpGet("planDay/{id}/getPlanDaysTypes")]
    [ProducesResponseType(typeof(List<PlanDayChooseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDaysTypes([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<UserEntity>.TryParse(id, out var routeUserId))
        {
            return Result<List<LgymApi.Domain.Entities.PlanDay>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDayService.GetPlanDaysTypesAsync(user!, routeUserId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var planDayDtos = _mapper.MapList<LgymApi.Domain.Entities.PlanDay, PlanDayChooseDto>(result.Value);
        return Ok(planDayDtos);
    }

    [HttpGet("planDay/{id}/deletePlanDay")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePlanDay([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanDayEntity>.TryParse(id, out var planDayId))
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDayService.DeletePlanDayAsync(user!, planDayId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpGet("planDay/{id}/getPlanDaysInfo")]
    [ProducesResponseType(typeof(List<PlanDayBaseInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDaysInfo([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<PlanDaysInfoContext, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDayService.GetPlanDaysInfoAsync(user!, planId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(PlanDayProfile.Keys.PlanDayExercises, context.PlanDayExercises);
        mappingContext.Set(PlanDayProfile.Keys.PlanDayLastTrainings, context.LastTrainingMap);
        var planDaysInfo = _mapper.MapList<LgymApi.Domain.Entities.PlanDay, PlanDayBaseInfoDto>(context.PlanDays, mappingContext);
        return Ok(planDaysInfo);
    }

}
