using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Nutrition.ApiAdapters;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerDietPlansController : ControllerBase
{
    private readonly IDietPlanAccountApiAdapter _dietPlans;
    private readonly IMapper _mapper;

    public TrainerDietPlansController(
        IDietPlanAccountApiAdapter dietPlans,
        IMapper mapper)
    {
        _dietPlans = dietPlans;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/diet-plans")]
    [ProducesResponseType(typeof(List<DietPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlans([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.GetTraineePlansAsync(new DietPlanListAccountQuery(trainerId, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/diet-plans/{dietPlanId}")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.GetTraineePlanAsync(new DietPlanGetAccountQuery(trainerId, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/diet-plans")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTraineePlan([FromRoute] string traineeId, [FromBody] UpsertDietPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.CreateAsync(
            new DietPlanCreateAccountCommand(trainerId, parsedTraineeId, _mapper.Map<UpsertDietPlanRequest, DietPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : StatusCode(StatusCodes.Status201Created, _mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/diet-plans/{dietPlanId}/update")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, [FromBody] UpsertDietPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.UpdateAsync(
            new DietPlanUpdateAccountCommand(trainerId, parsedTraineeId, parsedPlanId, _mapper.Map<UpsertDietPlanRequest, DietPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/diet-plans/{dietPlanId}/activate")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.ActivateAsync(new DietPlanActivateAccountCommand(trainerId, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainees/{traineeId}/diet-plans/{dietPlanId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.DeleteAsync(new DietPlanDeleteAccountCommand(trainerId, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpGet("trainees/{traineeId}/diet-plans/{dietPlanId}/history")]
    [ProducesResponseType(typeof(List<DietPlanHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlanHistory([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainerId = HttpContext.GetAuthenticatedAccountContext()!.Id;
        var result = await _dietPlans.GetHistoryAsync(new DietPlanHistoryAccountQuery(trainerId, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<DietPlanHistoryReadModel, DietPlanHistoryDto>(result.Value));
    }
}
