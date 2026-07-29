using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Exercise.Contracts;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Application.Features.Exercise.Models;
using LgymApi.Api.Middleware;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using ExerciseEntity = LgymApi.Domain.Entities.Exercise;
using LgymApi.Identity.Contracts;

namespace LgymApi.Api.Features.Exercise.Controllers;

public sealed partial class ExerciseController
{
    [HttpPost("exercise/{id}/getLastExerciseScores")]
    [ProducesResponseType(typeof(LastExerciseScoresResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLastExerciseScores([FromRoute] string id, [FromBody] LastExerciseScoresRequestDto request, CancellationToken cancellationToken = default)
    {
        var routeAccountId = id.ToIdOrEmpty<AccountReference>();
        var currentAccountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        var exerciseId = request.ExerciseId.ToIdOrEmpty<ExerciseEntity>();
        var gymId = request.GymId.ToNullableId<LgymApi.Domain.Entities.Gym>();

        var result = await _exerciseService.GetLastExerciseScoresAsync(routeAccountId, currentAccountId, exerciseId, request.Series, gymId, request.ExerciseName, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<LastExerciseScoresResult, LastExerciseScoresResponseDto>(result.Value));
    }

    [HttpPost("exercise/getExerciseScoresFromTrainingByExercise")]
    [ProducesResponseType(typeof(List<ExerciseTrainingHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExerciseScoresFromTrainingByExercise([FromBody] RecordOrPossibleRequestDto request, CancellationToken cancellationToken = default)
    {
        var currentAccountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        var exerciseId = request.ExerciseId.ToIdOrEmpty<ExerciseEntity>();
        var result = await _exerciseService.GetExerciseScoresFromTrainingByExerciseAsync(currentAccountId, exerciseId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.MapList<ExerciseTrainingHistoryItem, ExerciseTrainingHistoryItemDto>(result.Value);

        return Ok(mapped);
    }
}
