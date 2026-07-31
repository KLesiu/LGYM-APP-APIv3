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
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ApiAdapters;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Security;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Api.Features.Exercise.Controllers;

[ApiController]
[Route("api")]
public sealed partial class ExerciseController : ControllerBase
{
    private readonly IExerciseApiAdapter _exerciseService;
    private readonly IMapper _mapper;

    public ExerciseController(IExerciseApiAdapter exerciseService, IMapper mapper)
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
        var accountId = id.ToIdOrEmpty<AccountReference>();
        var result = await _exerciseService.AddUserExerciseAsync(accountId, form.Name, form.BodyPart, form.Description, form.Image, cancellationToken);
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
        var accountId = id.ToIdOrEmpty<AccountReference>();
        var input = _mapper.Map<ExerciseExtendedFormDto, AddExerciseWithFormulaInput>(form);
        var result = await _exerciseService.AddUserExerciseWithFormulaAsync(accountId, input, cancellationToken);
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

        var accountId = id.ToIdOrEmpty<AccountReference>();
        var exerciseId = exerciseIdString.ToIdOrEmpty<ExerciseEntity>();
        var result = await _exerciseService.DeleteExerciseAsync(accountId, exerciseId, cancellationToken);
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
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        if (currentAccount == null)
        {
            return Unauthorized();
        }

        var exerciseId = form.Id.ToIdOrEmpty<ExerciseEntity>();
        var input = new UpdateExerciseInput(exerciseId, form.Name, form.BodyPart, form.Description, form.Image);
        var result = await _exerciseService.UpdateExerciseAsync(currentAccount, input, cancellationToken);
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
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        if (currentAccount == null)
        {
            return Unauthorized();
        }

        var input = _mapper.Map<ExerciseExtendedFormDto, UpdateExerciseWithFormulaInput>(form);
        var result = await _exerciseService.UpdateExerciseWithFormulaAsync(currentAccount, input, cancellationToken);
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
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var routeAccountId = id.ToIdOrEmpty<AccountReference>();
        var exerciseId = form.ExerciseId.ToIdOrEmpty<ExerciseEntity>();
        var result = await _exerciseService.AddGlobalTranslationAsync(currentAccount, routeAccountId, exerciseId, form.Culture, form.Name, cancellationToken);
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
        var accountId = id.ToIdOrEmpty<AccountReference>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetAllExercisesAsync(accountId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel, ExerciseResponseDto>(context.Exercises, mappingContext);
        return Ok(response);
    }

    [HttpGet("exercise/{id}/getAllUserExercises")]
    [ProducesResponseType(typeof(List<ExerciseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAllUserExercises([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = id.ToIdOrEmpty<AccountReference>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetAllUserExercisesAsync(accountId, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel, ExerciseResponseDto>(context.Exercises, mappingContext);
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
        var response = _mapper.MapList<LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel, ExerciseResponseDto>(context.Exercises, mappingContext);
        return Ok(response);
    }

    [HttpPost("exercise/{id}/getExerciseByBodyPart")]
    [ProducesResponseType(typeof(List<ExerciseResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExerciseByBodyPart([FromRoute] string id, [FromBody] ExerciseByBodyPartRequestDto request, CancellationToken cancellationToken = default)
    {
        var accountId = id.ToIdOrEmpty<AccountReference>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetExerciseByBodyPartAsync(accountId, request.BodyPart, cultures, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, context.Translations);
        var response = _mapper.MapList<LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel, ExerciseResponseDto>(context.Exercises, mappingContext);
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
        return Ok(_mapper.Map<LgymApi.Application.WorkoutProgress.ProgressData.Models.ProgressExerciseReadModel, ExerciseResponseDto>(context.Exercise, mappingContext));
    }

}
