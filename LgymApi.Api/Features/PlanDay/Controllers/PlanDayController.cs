using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Extensions;
using LgymApi.Api.Features.PlanDay.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Contracts.PlanDay;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Mvc;
using LgymApi.TrainingPlanning.Contracts;

namespace LgymApi.Api.Features.PlanDay.Controllers;

[ApiController]
[Route("api")]
public sealed class PlanDayController : ControllerBase
{
    private readonly IPlanDayService _planDays;
    private readonly IMapper _mapper;

    public PlanDayController(IPlanDayService planDays, IMapper mapper)
    {
        _planDays = planDays;
        _mapper = mapper;
    }

    [HttpPost("planDay/{id}/createPlanDay")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePlanDay([FromRoute] string id, [FromBody] PlanDayFormDto form, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<PlanReference>.TryParse(id, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDays.CreateAsync(
            new CreatePlanDayCommand(accountId, planId, _mapper.Map<PlanDayFormDto, PlanDayWriteModel>(form)),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("planDay/updatePlanDay")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlanDay([FromBody] PlanDayFormDto form, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<PlanDayReference>.TryParse(form.Id ?? string.Empty, out var planDayId))
        {
            return Result<Unit, AppError>.Failure(new InvalidPlanDayError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDays.UpdateAsync(
            new UpdatePlanDayCommand(accountId, planDayId, _mapper.Map<PlanDayFormDto, PlanDayWriteModel>(form)),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("planDay/{id}/getPlanDay")]
    [ProducesResponseType(typeof(PlanDayVmDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDay([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<PlanDayReference>.TryParse(id, out var planDayId))
        {
            return Result<PlanDayReadModel, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var cultures = HttpContext.GetCulturePreferences();
        var result = await _planDays.GetAsync(new GetPlanDayQuery(accountId, planDayId, cultures), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var planDayVm = _mapper.Map<PlanDayReadModel, PlanDayVmDto>(result.Value);
        return Ok(planDayVm);
    }

    [HttpGet("planDay/{id}/getPlanDays")]
    [ProducesResponseType(typeof(List<PlanDayVmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDays([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<PlanReference>.TryParse(id, out var planId))
        {
            return Result<IReadOnlyList<PlanDayReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var cultures = HttpContext.GetCulturePreferences();
        var result = await _planDays.GetForPlanAsync(new GetPlanDaysQuery(accountId, planId, cultures), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var planDays = _mapper.MapList<PlanDayReadModel, PlanDayVmDto>(result.Value);
        return Ok(planDays);
    }

    [HttpGet("planDay/{id}/getPlanDaysTypes")]
    [ProducesResponseType(typeof(List<PlanDayChooseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDaysTypes([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<AccountReference>.TryParse(id, out var routeAccountId))
        {
            return Result<IReadOnlyList<PlanDayChoiceReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDays.GetTypesAsync(new GetPlanDayTypesQuery(accountId, routeAccountId), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var planDayDtos = _mapper.MapList<PlanDayChoiceReadModel, PlanDayChooseDto>(result.Value);
        return Ok(planDayDtos);
    }

    [HttpGet("planDay/{id}/deletePlanDay")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePlanDay([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<PlanDayReference>.TryParse(id, out var planDayId))
        {
            return Result<Unit, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDays.DeleteAsync(new DeletePlanDayCommand(accountId, planDayId), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpGet("planDay/{id}/getPlanDaysInfo")]
    [ProducesResponseType(typeof(List<PlanDayBaseInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanDaysInfo([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        if (!Id<PlanReference>.TryParse(id, out var planId))
        {
            return Result<IReadOnlyList<PlanDayInfoReadModel>, AppError>.Failure(new PlanDayNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _planDays.GetInfoAsync(new GetPlanDaysInfoQuery(accountId, planId), cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var planDaysInfo = _mapper.MapList<PlanDayInfoReadModel, PlanDayBaseInfoDto>(result.Value);
        return Ok(planDaysInfo);
    }

}
