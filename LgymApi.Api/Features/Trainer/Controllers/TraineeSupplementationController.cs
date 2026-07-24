using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Common.Errors;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Contracts;
using LgymApi.Application.Nutrition.Supplementation.CheckOffIntake.Models;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule.Contracts;
using LgymApi.Application.Nutrition.Supplementation.GetSchedule.Models;
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
    private readonly IGetSupplementScheduleUseCase _getSupplementSchedule;
    private readonly ICheckOffSupplementIntakeUseCase _checkOffSupplementIntake;
    private readonly IMapper _mapper;

    public TraineeSupplementationController(
        IGetSupplementScheduleUseCase getSupplementSchedule,
        ICheckOffSupplementIntakeUseCase checkOffSupplementIntake,
        IMapper mapper)
    {
        _getSupplementSchedule = getSupplementSchedule;
        _checkOffSupplementIntake = checkOffSupplementIntake;
        _mapper = mapper;
    }

    [HttpGet("supplements/schedule")]
    [ProducesResponseType(typeof(List<SupplementScheduleEntryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSchedule([FromQuery] DateOnly? date, CancellationToken cancellationToken = default)
    {
        var result = await _getSupplementSchedule.ExecuteAsync(
            new GetSupplementScheduleQuery(
                HttpContext.GetCurrentUserId(),
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
        
        var result = await _checkOffSupplementIntake.ExecuteAsync(
            new CheckOffSupplementIntakeCommand(
                HttpContext.GetCurrentUserId(),
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
