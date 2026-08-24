using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.ApiAdapters;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainee")]
[Authorize]
public sealed class TraineeDietPlanController : ControllerBase
{
    private readonly IDietPlanAccountApiAdapter _dietPlans;
    private readonly IMapper _mapper;

    public TraineeDietPlanController(
        IDietPlanAccountApiAdapter dietPlans,
        IMapper mapper)
    {
        _dietPlans = dietPlans;
        _mapper = mapper;
    }

    [HttpGet("diet-plans/current")]
    [ProducesResponseType(typeof(List<DietPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentPlans(CancellationToken cancellationToken = default)
    {
        var result = await _dietPlans.GetCurrentPlansAsync(
            new DietPlanCurrentAccountQuery(HttpContext.GetAuthenticatedAccountContext()!.Id),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.MapList<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpGet("diet-plan/current")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrentPlan(CancellationToken cancellationToken = default)
    {
        var result = await _dietPlans.GetCurrentPlanAsync(
            new DietPlanCurrentAccountQuery(HttpContext.GetAuthenticatedAccountContext()!.Id),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpGet("diet-plans/{dietPlanId}/history")]
    [ProducesResponseType(typeof(List<DietPlanHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOwnPlanHistory(
        [FromRoute] string dietPlanId,
        CancellationToken cancellationToken = default)
    {
        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, LgymApi.Api.Features.Common.Contracts.ResponseMessageDto>(Messages.FieldRequired));
        }

        var traineeId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.GetOwnHistoryAsync(
            traineeId,
            parsedPlanId,
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.MapList<DietPlanHistoryReadModel, DietPlanHistoryDto>(result.Value));
    }
}
