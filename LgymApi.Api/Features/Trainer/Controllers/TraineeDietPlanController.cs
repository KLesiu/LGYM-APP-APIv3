using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.Nutrition.DietPlans.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainee")]
[Authorize]
public sealed class TraineeDietPlanController : ControllerBase
{
    private readonly IDietPlanAccountCompatibilityAdapter _dietPlans;
    private readonly IMapper _mapper;

    public TraineeDietPlanController(
        IDietPlanAccountCompatibilityAdapter dietPlans,
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
}
