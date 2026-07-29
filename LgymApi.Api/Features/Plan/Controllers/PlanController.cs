using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Plan.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Identity.Compatibility.Task7.Contracts;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;
using PlanEntity = LgymApi.Domain.Entities.Plan;

namespace LgymApi.Api.Features.Plan.Controllers;

[ApiController]
[Route("api")]
public sealed class PlanController : ControllerBase
{
    private readonly IPlanAccountCompatibilityAdapter _plans;
    private readonly IMapper _mapper;

    public PlanController(
        IPlanAccountCompatibilityAdapter plans,
        IMapper mapper)
    {
        _plans = plans;
        _mapper = mapper;
    }

    [HttpPost("{id}/createPlan")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePlan([FromRoute] string id, [FromBody] PlanFormDto form, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<AccountReference>.TryParse(id, out var routeAccountId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.CreateAsync(
            new PlanCreateAccountCommand(accountId, routeAccountId, form.Name),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("{id}/updatePlan")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePlan([FromRoute] string id, [FromBody] PlanFormDto form, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<AccountReference>.TryParse(id, out var routeAccountId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(form.Id ?? string.Empty, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.UpdateAsync(
            new PlanUpdateAccountCommand(accountId, routeAccountId, planId, form.Name),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpGet("{id}/getPlanConfig")]
    [ProducesResponseType(typeof(PlanFormDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlanConfig([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<AccountReference>.TryParse(id, out var routeAccountId))
        {
            return Result<PlanReadModel, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.GetConfigAsync(
            new PlanGetConfigAccountQuery(accountId, routeAccountId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<PlanReadModel, PlanFormDto>(result.Value));
    }

    [HttpGet("{id}/checkIsUserHavePlan")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(bool), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckIsUserHavePlan([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        var routeAccountId = Id<AccountReference>.TryParse(id, out var parsedAccountId) ? parsedAccountId : Id<AccountReference>.Empty;
        var result = await _plans.HasPlanAsync(
            new PlanHasAccountQuery(accountId, routeAccountId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(result.Value);
    }

    [HttpGet("{id}/getPlansList")]
    [ProducesResponseType(typeof(List<PlanFormDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlansList([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<AccountReference>.TryParse(id, out var routeAccountId))
        {
            return Result<List<PlanReadModel>, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.GetListAsync(
            new PlanGetListAccountQuery(accountId, routeAccountId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mapped = _mapper.MapList<PlanReadModel, PlanFormDto>(result.Value);
        return Ok(mapped);
    }

    [HttpPost("{id}/setNewActivePlan")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetNewActivePlan([FromRoute] string id, [FromBody] SetActivePlanDto form, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<AccountReference>.TryParse(id, out var routeAccountId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(form.Id ?? string.Empty, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.SetActiveAsync(
            new PlanSetActiveAccountCommand(accountId, routeAccountId, planId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("copy")]
    [ProducesResponseType(typeof(PlanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CopyPlan([FromBody] CopyPlanDto dto, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        var result = await _plans.CopyAsync(
            new PlanCopyAccountCommand(accountId, dto.ShareCode),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var planDto = _mapper.Map<PlanReadModel, PlanDto>(result.Value);
        return StatusCode(StatusCodes.Status201Created, planDto);
    }

    [HttpPost("{id}/share")]
    [ProducesResponseType(typeof(ShareCodeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateShareCode([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<string, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.GenerateShareCodeAsync(
            new PlanGenerateShareCodeAccountCommand(accountId, planId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ShareCodeResponseDto>(result.Value));
    }

    [HttpPost("{id}/deletePlan")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePlan([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _plans.DeleteAsync(
            new PlanDeleteAccountCommand(accountId, planId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }
}
