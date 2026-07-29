using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Tutorial.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Identity.ApiCompatibility;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Tutorial.Controllers;

[ApiController]
[Route("api/tutorials")]
public sealed class TutorialController : ControllerBase
{
    private readonly IAccountTutorialApiAdapter _accountTutorialApiAdapter;
    private readonly IMapper _mapper;

    public TutorialController(IAccountTutorialApiAdapter accountTutorialApiAdapter, IMapper mapper)
    {
        _accountTutorialApiAdapter = accountTutorialApiAdapter;
        _mapper = mapper;
    }

    [HttpGet("active")]
    [ProducesResponseType(typeof(List<TutorialProgressDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveTutorials(CancellationToken cancellationToken = default)
    {
        var result = await _accountTutorialApiAdapter.GetActiveTutorialsAsync(GetCurrentAccountId(), cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.MapList<TutorialProgressProjection, TutorialProgressDto>(result.Value);
        return Ok(mapped);
    }

    [HttpGet("{tutorialType}")]
    [ProducesResponseType(typeof(TutorialProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTutorialProgress([FromRoute] TutorialType tutorialType, CancellationToken cancellationToken = default)
    {
        var result = await _accountTutorialApiAdapter.GetTutorialProgressAsync(GetCurrentAccountId(), tutorialType, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        if (result.Value == null)
        {
            return NotFound();
        }

        var mapped = _mapper.Map<TutorialProgressProjection, TutorialProgressDto>(result.Value);
        return Ok(mapped);
    }

    [HttpPost("completeStep")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteStep([FromBody] CompleteStepRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _accountTutorialApiAdapter.CompleteStepAsync(GetCurrentAccountId(), request.TutorialType, request.Step, cancellationToken);

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
        var result = await _accountTutorialApiAdapter.CompleteTutorialAsync(GetCurrentAccountId(), request.TutorialType, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    private Id<AccountReference> GetCurrentAccountId()
        => HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
}
