using System.Globalization;
using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.ExerciseScores.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.ExerciseScores;
using LgymApi.Application.Features.ExerciseScores.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.ExerciseScores.Controllers;

[ApiController]
[Route("api")]
public sealed class ExerciseScoresController : ControllerBase
{
    private readonly IExerciseScoresService _exerciseScoresService;
    private readonly IMapper _mapper;

    public ExerciseScoresController(IExerciseScoresService exerciseScoresService, IMapper mapper)
    {
        _exerciseScoresService = exerciseScoresService;
        _mapper = mapper;
    }

    [HttpPost("exerciseScores/{id}/getExerciseScoresChartData")]
    [ProducesResponseType(typeof(List<ExerciseScoresChartDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetExerciseScoresChartData([FromRoute] string id, [FromBody] ExerciseScoresChartRequestDto request, CancellationToken cancellationToken = default)
    {
        var userId = HttpContext.ParseRouteUserIdForCurrentUser(id);
        Id<LgymApi.Domain.Entities.Exercise>.TryParse(request.ExerciseId, out var parsedExerciseId);
        var result = await _exerciseScoresService.GetExerciseScoresChartDataAsync(userId, parsedExerciseId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.MapList<ExerciseScoresChartData, ExerciseScoresChartDataDto>(result.Value);
        return Ok(mapped);
    }
}
