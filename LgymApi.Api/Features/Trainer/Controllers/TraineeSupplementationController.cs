using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.Nutrition.Supplementation.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainee")]
[Authorize]
public sealed class TraineeSupplementationController : ControllerBase
{
    private readonly ISupplementationAccountCompatibilityAdapter _supplementation;
    private readonly IMapper _mapper;

    public TraineeSupplementationController(
        ISupplementationAccountCompatibilityAdapter supplementation,
        IMapper mapper)
    {
        _supplementation = supplementation;
        _mapper = mapper;
    }

    [HttpGet("supplements/schedule")]
    [ProducesResponseType(typeof(List<SupplementScheduleEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSchedule([FromQuery] DateOnly? date, CancellationToken cancellationToken = default)
    {
        var result = await _supplementation.GetScheduleAsync(
            new SupplementScheduleAccountQuery(
                HttpContext.GetAuthenticatedAccountContext()!.Id,
                date ?? DateOnly.FromDateTime(DateTime.UtcNow)),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.MapList<SupplementScheduleEntryReadModel, SupplementScheduleEntryDto>(result.Value));
    }

    [HttpPost("supplements/intakes/check-off")]
    [ProducesResponseType(typeof(SupplementScheduleEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CheckOffIntake([FromBody] CheckOffSupplementIntakeRequest request, CancellationToken cancellationToken = default)
    {
        Id<LgymApi.Domain.Entities.SupplementPlanItem>.TryParse(request.PlanItemId, out var parsedPlanItemId);

        var result = await _supplementation.CheckOffAsync(
            new SupplementCheckOffAccountCommand(
                HttpContext.GetAuthenticatedAccountContext()!.Id,
                parsedPlanItemId,
                request.IntakeDate,
                request.TakenAt),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<SupplementScheduleEntryReadModel, SupplementScheduleEntryDto>(result.Value));
    }
}
