using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Plan.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.TrainingPlanning.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.TrainingPlanning.Plan.CheckIsUserHavePlan;
using LgymApi.Application.TrainingPlanning.Plan.CopyPlan;
using LgymApi.Application.TrainingPlanning.Plan.CreatePlan;
using LgymApi.Application.TrainingPlanning.Plan.DeletePlan;
using LgymApi.Application.TrainingPlanning.Plan.GenerateShareCode;
using LgymApi.Application.TrainingPlanning.Plan.GetPlanConfig;
using LgymApi.Application.TrainingPlanning.Plan.GetPlansList;
using LgymApi.Application.TrainingPlanning.Plan.Models;
using LgymApi.Application.TrainingPlanning.Plan.SetActivePlan;
using LgymApi.Application.TrainingPlanning.Plan.UpdatePlan;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using Microsoft.AspNetCore.Mvc;
using PlanEntity = LgymApi.Domain.Entities.Plan;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Api.Features.Plan.Controllers;

[ApiController]
[Route("api")]
public sealed class PlanController : ControllerBase
{
    private readonly ICreatePlanUseCase _createPlanUseCase;
    private readonly IUpdatePlanUseCase _updatePlanUseCase;
    private readonly IDeletePlanUseCase _deletePlanUseCase;
    private readonly IGetPlanConfigUseCase _getPlanConfigUseCase;
    private readonly IGetPlansListUseCase _getPlansListUseCase;
    private readonly ISetActivePlanUseCase _setActivePlanUseCase;
    private readonly ICopyPlanUseCase _copyPlanUseCase;
    private readonly IGenerateShareCodeUseCase _generateShareCodeUseCase;
    private readonly ICheckIsUserHavePlanUseCase _checkIsUserHavePlanUseCase;
    private readonly IMapper _mapper;

    public PlanController(
        ICreatePlanUseCase createPlanUseCase,
        IUpdatePlanUseCase updatePlanUseCase,
        IDeletePlanUseCase deletePlanUseCase,
        IGetPlanConfigUseCase getPlanConfigUseCase,
        IGetPlansListUseCase getPlansListUseCase,
        ISetActivePlanUseCase setActivePlanUseCase,
        ICopyPlanUseCase copyPlanUseCase,
        IGenerateShareCodeUseCase generateShareCodeUseCase,
        ICheckIsUserHavePlanUseCase checkIsUserHavePlanUseCase,
        IMapper mapper)
    {
        _createPlanUseCase = createPlanUseCase;
        _updatePlanUseCase = updatePlanUseCase;
        _deletePlanUseCase = deletePlanUseCase;
        _getPlanConfigUseCase = getPlanConfigUseCase;
        _getPlansListUseCase = getPlansListUseCase;
        _setActivePlanUseCase = setActivePlanUseCase;
        _copyPlanUseCase = copyPlanUseCase;
        _generateShareCodeUseCase = generateShareCodeUseCase;
        _checkIsUserHavePlanUseCase = checkIsUserHavePlanUseCase;
        _mapper = mapper;
    }

    [HttpPost("{id}/createPlan")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePlan([FromRoute] string id, [FromBody] PlanFormDto form, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        if (!Id<UserEntity>.TryParse(id, out var routeUserId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _createPlanUseCase.ExecuteAsync(
            new CreatePlanCommand(user?.Id ?? Id<UserEntity>.Empty, routeUserId, form.Name),
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
        var user = HttpContext.GetCurrentUser();
        if (!Id<UserEntity>.TryParse(id, out var routeUserId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(form.Id ?? string.Empty, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _updatePlanUseCase.ExecuteAsync(
            new UpdatePlanCommand(user?.Id ?? Id<UserEntity>.Empty, routeUserId, planId, form.Name),
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
        var user = HttpContext.GetCurrentUser();
        if (!Id<UserEntity>.TryParse(id, out var routeUserId))
        {
            return Result<PlanReadModel, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _getPlanConfigUseCase.ExecuteAsync(
            new GetPlanConfigQuery(user?.Id ?? Id<UserEntity>.Empty, routeUserId),
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
        var user = HttpContext.GetCurrentUser();
        var routeUserId = Id<UserEntity>.TryParse(id, out var parsedUserId) ? parsedUserId : Id<UserEntity>.Empty;
        var result = await _checkIsUserHavePlanUseCase.ExecuteAsync(
            new CheckIsUserHavePlanQuery(user?.Id ?? Id<UserEntity>.Empty, routeUserId),
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
        var user = HttpContext.GetCurrentUser();
        if (!Id<UserEntity>.TryParse(id, out var routeUserId))
        {
            return Result<List<PlanReadModel>, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _getPlansListUseCase.ExecuteAsync(
            new GetPlansListQuery(user?.Id ?? Id<UserEntity>.Empty, routeUserId),
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
        var user = HttpContext.GetCurrentUser();
        if (!Id<UserEntity>.TryParse(id, out var routeUserId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        if (!Id<PlanEntity>.TryParse(form.Id ?? string.Empty, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _setActivePlanUseCase.ExecuteAsync(
            new SetActivePlanCommand(user?.Id ?? Id<UserEntity>.Empty, routeUserId, planId),
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
        var user = HttpContext.GetCurrentUser();
        var result = await _copyPlanUseCase.ExecuteAsync(
            new CopyPlanCommand(user?.Id ?? Id<UserEntity>.Empty, dto.ShareCode),
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
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<string, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _generateShareCodeUseCase.ExecuteAsync(
            new GenerateShareCodeCommand(user?.Id ?? Id<UserEntity>.Empty, planId),
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
        var user = HttpContext.GetCurrentUser();
        if (!Id<PlanEntity>.TryParse(id, out var planId))
        {
            return Result<Unit, AppError>.Failure(new PlanNotFoundError(Messages.DidntFind)).ToActionResult();
        }

        var result = await _deletePlanUseCase.ExecuteAsync(
            new DeletePlanCommand(user?.Id ?? Id<UserEntity>.Empty, planId),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }
}
