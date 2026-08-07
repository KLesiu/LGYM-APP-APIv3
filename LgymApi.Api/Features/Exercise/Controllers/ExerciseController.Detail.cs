using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Api.Middleware;
using LgymApi.Application.WorkoutProgress.ApiAdapters;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;

namespace LgymApi.Api.Features.Exercise.Controllers;

public sealed partial class ExerciseController
{
    [HttpGet("exercise/{id}/getExercise")]
    [ProducesResponseType(typeof(ExerciseResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExercise([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var exerciseId = id.ToIdOrEmpty<ExerciseEntity>();
        var cultures = HttpContext.GetCulturePreferences();
        var result = await _exerciseService.GetExerciseAsync(currentAccount, exerciseId, cultures, cancellationToken);
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
