using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Tutorial.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.Tutorial;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Tutorial.Controllers;

[ApiController]
[Route("api/tutorials")]
public sealed class TutorialController : ControllerBase
{
    private readonly ITutorialService _tutorialService;
    private readonly IMapper _mapper;

    public TutorialController(ITutorialService tutorialService, IMapper mapper)
    {
        _tutorialService = tutorialService;
        _mapper = mapper;
    }

    [HttpGet("active")]
    [ProducesResponseType(typeof(List<TutorialProgressDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveTutorials(CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        var result = await _tutorialService.GetActiveTutorialsAsync(user!.Id, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.MapList<Application.Features.Tutorial.Models.TutorialProgressResult, TutorialProgressDto>(result.Value);
        return Ok(mapped);
    }

    [HttpGet("{tutorialType}")]
    [ProducesResponseType(typeof(TutorialProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTutorialProgress([FromRoute] TutorialType tutorialType, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        var result = await _tutorialService.GetTutorialProgressAsync(user!.Id, tutorialType, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        if (result.Value == null)
        {
            return NotFound();
        }

        var mapped = _mapper.Map<Application.Features.Tutorial.Models.TutorialProgressResult, TutorialProgressDto>(result.Value);
        return Ok(mapped);
    }

    [HttpPost("completeStep")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteStep([FromBody] CompleteStepRequest request, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        var result = await _tutorialService.CompleteStepAsync(user!.Id, request.TutorialType, request.Step, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("complete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteTutorial([FromBody] CompleteTutorialRequest request, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        var result = await _tutorialService.CompleteTutorialAsync(user!.Id, request.TutorialType, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
