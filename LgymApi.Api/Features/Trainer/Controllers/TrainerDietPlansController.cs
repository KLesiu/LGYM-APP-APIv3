using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.ActivateTraineePlan.Models;
using LgymApi.Application.Nutrition.DietPlans.CreateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Contracts;
using LgymApi.Application.Nutrition.DietPlans.DeleteTraineePlan.Models;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Contracts;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlanHistory.Models;
using LgymApi.Application.Nutrition.DietPlans.GetTraineePlans;
using LgymApi.Application.Nutrition.DietPlans.Models;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan;
using LgymApi.Application.Nutrition.DietPlans.UpdateTraineePlan.Contracts;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainer")]
[Authorize(Policy = AuthConstants.Policies.TrainerAccess)]
public sealed class TrainerDietPlansController : ControllerBase
{
    private readonly IGetTraineeDietPlansUseCase _getTraineePlans;
    private readonly IGetTraineeDietPlanUseCase _getTraineePlan;
    private readonly ICreateTraineeDietPlanUseCase _createTraineePlan;
    private readonly IUpdateTraineeDietPlanUseCase _updateTraineePlan;
    private readonly IActivateTraineeDietPlanUseCase _activateTraineePlan;
    private readonly IDeleteTraineeDietPlanUseCase _deleteTraineePlan;
    private readonly IGetTraineeDietPlanHistoryUseCase _getTraineePlanHistory;
    private readonly IMapper _mapper;

    public TrainerDietPlansController(
        IGetTraineeDietPlansUseCase getTraineePlans,
        IGetTraineeDietPlanUseCase getTraineePlan,
        ICreateTraineeDietPlanUseCase createTraineePlan,
        IUpdateTraineeDietPlanUseCase updateTraineePlan,
        IActivateTraineeDietPlanUseCase activateTraineePlan,
        IDeleteTraineeDietPlanUseCase deleteTraineePlan,
        IGetTraineeDietPlanHistoryUseCase getTraineePlanHistory,
        IMapper mapper)
    {
        _getTraineePlans = getTraineePlans;
        _getTraineePlan = getTraineePlan;
        _createTraineePlan = createTraineePlan;
        _updateTraineePlan = updateTraineePlan;
        _activateTraineePlan = activateTraineePlan;
        _deleteTraineePlan = deleteTraineePlan;
        _getTraineePlanHistory = getTraineePlanHistory;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/diet-plans")]
    [ProducesResponseType(typeof(List<DietPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlans([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getTraineePlans.ExecuteAsync(new GetTraineeDietPlansQuery(trainer!.Id, parsedTraineeId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpGet("trainees/{traineeId}/diet-plans/{dietPlanId}")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getTraineePlan.ExecuteAsync(new GetTraineeDietPlanQuery(trainer!.Id, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/diet-plans")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateTraineePlan([FromRoute] string traineeId, [FromBody] UpsertDietPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _createTraineePlan.ExecuteAsync(
            new CreateTraineeDietPlanCommand(
                trainer!.Id,
                parsedTraineeId,
                _mapper.Map<UpsertDietPlanRequest, DietPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : StatusCode(StatusCodes.Status201Created, _mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/diet-plans/{dietPlanId}/update")]
    [ProducesResponseType(typeof(DietPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, [FromBody] UpsertDietPlanRequest request, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _updateTraineePlan.ExecuteAsync(
            new UpdateTraineeDietPlanCommand(
                trainer!.Id,
                parsedTraineeId,
                parsedPlanId,
                _mapper.Map<UpsertDietPlanRequest, DietPlanUpsertData>(request)),
            cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<DietPlanReadModel, DietPlanDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/diet-plans/{dietPlanId}/activate")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ActivateTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _activateTraineePlan.ExecuteAsync(new ActivateTraineeDietPlanCommand(trainer!.Id, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("trainees/{traineeId}/diet-plans/{dietPlanId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteTraineePlan([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _deleteTraineePlan.ExecuteAsync(new DeleteTraineeDietPlanCommand(trainer!.Id, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpGet("trainees/{traineeId}/diet-plans/{dietPlanId}/history")]
    [ProducesResponseType(typeof(List<DietPlanHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTraineePlanHistory([FromRoute] string traineeId, [FromRoute] string dietPlanId, CancellationToken cancellationToken = default)
    {
        if (!Id<UserEntity>.TryParse(traineeId, out var parsedTraineeId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
        }

        if (!Id<DietPlan>.TryParse(dietPlanId, out var parsedPlanId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var trainer = HttpContext.GetCurrentUser();
        var result = await _getTraineePlanHistory.ExecuteAsync(new GetTraineeDietPlanHistoryQuery(trainer!.Id, parsedTraineeId, parsedPlanId), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<DietPlanHistoryReadModel, DietPlanHistoryDto>(result.Value));
    }
}
