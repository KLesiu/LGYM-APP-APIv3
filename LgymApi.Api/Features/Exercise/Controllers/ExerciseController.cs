using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Enum;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.WorkoutProgress.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Exercise;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Exercise.Controllers;

[ApiController]
[Route("api")]
public sealed partial class ExerciseController : ControllerBase
{
    private readonly IExerciseService _exerciseService;
    private readonly IMapper _mapper;

    public ExerciseController(IExerciseService exerciseService, IMapper mapper)
    {
        _exerciseService = exerciseService;
        _mapper = mapper;
    }

    [HttpPost("exercise/addExercise")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddExercise([FromBody] ExerciseFormDto form, CancellationToken cancellationToken = default)
    {
        var result = await _exerciseService.AddExerciseAsync(form.Name, form.BodyPart, form.Description, form.Image, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("exercise/addExerciseWithFormula")]
    [Authorize(Policy = AuthConstants.Policies.ManageGlobalExercises)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddExerciseWithFormula([FromBody] ExerciseExtendedFormDto form, CancellationToken cancellationToken = default)
    {
        var input = _mapper.Map<ExerciseExtendedFormDto, AddExerciseWithFormulaInput>(form);
        var result = await _exerciseService.AddExerciseWithFormulaAsync(input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("exercise/{id}/addUserExercise")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddUserExercise([FromRoute] string id, [FromBody] ExerciseFormDto form, CancellationToken cancellationToken = default)
    {
        var userId = id.ToIdOrEmpty<UserEntity>();
        var input = new AddUserExerciseInput(userId, form.Name, form.BodyPart, form.Description, form.Image);
        var result = await _exerciseService.AddUserExerciseAsync(input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("exercise/{id}/addUserExerciseWithFormula")]
    [Authorize(Policy = AuthConstants.Policies.ManageGlobalExercises)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddUserExerciseWithFormula([FromRoute] string id, [FromBody] ExerciseExtendedFormDto form, CancellationToken cancellationToken = default)
    {
        var userId = id.ToIdOrEmpty<UserEntity>();
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.UserId, userId);
        var input = mappingContext.Map<ExerciseExtendedFormDto, AddUserExerciseWithFormulaInput>(form);
        var result = await _exerciseService.AddUserExerciseWithFormulaAsync(input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("exercise/{id}/deleteExercise")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteExercise([FromRoute] string id, [FromBody] Dictionary<string, string> body, CancellationToken cancellationToken = default)
    {
        if (!body.TryGetValue("id", out var exerciseIdString))
        {
            return Result<Unit, AppError>.Failure(new InvalidExerciseError(Messages.FieldRequired)).ToActionResult();
        }

        var userId = id.ToIdOrEmpty<UserEntity>();
        var exerciseId = exerciseIdString.ToIdOrEmpty<ExerciseEntity>();
        var result = await _exerciseService.DeleteExerciseAsync(userId, exerciseId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("exercise/updateExercise")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateExercise([FromBody] ExerciseFormDto form, CancellationToken cancellationToken = default)
    {
        var currentUser = HttpContext.GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var exerciseId = form.Id.ToIdOrEmpty<ExerciseEntity>();
        var input = new UpdateExerciseInput(exerciseId, form.Name, form.BodyPart, form.Description, form.Image);
        var result = await _exerciseService.UpdateExerciseAsync(currentUser, input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("exercise/updateExerciseWithFormula")]
    [Authorize(Policy = AuthConstants.Policies.ManageGlobalExercises)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateExerciseWithFormula([FromBody] ExerciseExtendedFormDto form, CancellationToken cancellationToken = default)
    {
        var currentUser = HttpContext.GetCurrentUser();
        if (currentUser == null)
        {
            return Unauthorized();
        }

        var input = _mapper.Map<ExerciseExtendedFormDto, UpdateExerciseWithFormulaInput>(form);
        var result = await _exerciseService.UpdateExerciseWithFormulaAsync(currentUser, input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("exercise/{id}/addGlobalTranslation")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGlobalTranslation([FromRoute] string id, [FromBody] ExerciseTranslationDto form, CancellationToken cancellationToken = default)
    {
        var currentUser = HttpContext.GetCurrentUser();
        var routeUserId = id.ToIdOrEmpty<UserEntity>();
        var exerciseId = form.ExerciseId.ToIdOrEmpty<ExerciseEntity>();
        var input = new AddGlobalTranslationInput(routeUserId, exerciseId, form.Culture, form.Name);
        var result = await _exerciseService.AddGlobalTranslationAsync(currentUser!, input, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("exercise/{id}/getAllExercises")]
    [ProducesResponseType(typeof(List<ExerciseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllExercises([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var userId = id.ToIdOrEmpty<UserEntity>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetAllExercisesAsync(userId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<ExerciseEntity, ExerciseResponseDto>(context.Exercises, mappingContext);
        return Ok(response);
    }

    [HttpGet("exercise/{id}/getAllUserExercises")]
    [ProducesResponseType(typeof(List<ExerciseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllUserExercises([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var userId = id.ToIdOrEmpty<UserEntity>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetAllUserExercisesAsync(userId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<ExerciseEntity, ExerciseResponseDto>(context.Exercises, mappingContext);
        return Ok(response);
    }

    [HttpGet("exercise/getAllGlobalExercises")]
    [ProducesResponseType(typeof(List<ExerciseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllGlobalExercises(CancellationToken cancellationToken = default)
    {
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetAllGlobalExercisesAsync(cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<ExerciseEntity, ExerciseResponseDto>(context.Exercises, mappingContext);
        return Ok(response);
    }

    [HttpPost("exercise/{id}/getExerciseByBodyPart")]
    [ProducesResponseType(typeof(List<ExerciseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExerciseByBodyPart([FromRoute] string id, [FromBody] ExerciseByBodyPartRequestDto request, CancellationToken cancellationToken = default)
    {
        var userId = id.ToIdOrEmpty<UserEntity>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetExerciseByBodyPartAsync(userId, request.BodyPart, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<ExerciseEntity, ExerciseResponseDto>(context.Exercises, mappingContext);
        return Ok(response);
    }

    [HttpGet("exercise/{id}/getExercise")]
    [ProducesResponseType(typeof(ExerciseResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExercise([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var exerciseId = id.ToIdOrEmpty<ExerciseEntity>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetExerciseAsync(exerciseId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        return Ok(_mapper.Map<ExerciseEntity, ExerciseResponseDto>(context.Exercise, mappingContext));
    }

}
