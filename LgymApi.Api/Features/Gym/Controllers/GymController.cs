using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Gym.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.Features.Gym;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Gym.Controllers;

[ApiController]
[Route("api")]
public sealed class GymController : ControllerBase
{
    private readonly IGymService _gymService;
    private readonly IMapper _mapper;

    public GymController(IGymService gymService, IMapper mapper)
    {
        _gymService = gymService;
        _mapper = mapper;
    }

    [HttpPost("gym/{id}/addGym")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGym([FromRoute] string id, [FromBody] GymFormDto form, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var routeAccountId = Id<AccountReference>.TryParse(id, out var parsedAccountId) ? parsedAccountId : Id<AccountReference>.Empty;

        var result = await _gymService.AddGymAsync(currentAccount, routeAccountId, form.Name, form.Address, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("gym/{id}/deleteGym")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteGym([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var gymId = Id<LgymApi.Domain.Entities.Gym>.TryParse(id, out var parsedGymId) ? parsedGymId : Id<LgymApi.Domain.Entities.Gym>.Empty;

        var result = await _gymService.DeleteGymAsync(currentAccount, gymId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpGet("gym/{id}/getGyms")]
    [ProducesResponseType(typeof(List<GymChoiceInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGyms([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var routeAccountId = Id<AccountReference>.TryParse(id, out var parsedAccountId) ? parsedAccountId : Id<AccountReference>.Empty;

        var result = await _gymService.GetGymsAsync(currentAccount, routeAccountId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(GymProfile.Keys.LastTrainingMap, context.LastTrainings);
        mappingContext.Set(GymProfile.Keys.PlanDayMap, context.PlanDays);

        var gyms = _mapper.MapList<LgymApi.Application.WorkoutProgress.Persistence.WorkoutGymPersistenceModel, GymChoiceInfoDto>(context.Gyms, mappingContext);

        return Ok(gyms);
    }

    [HttpGet("gym/{id}/getGym")]
    [ProducesResponseType(typeof(GymFormDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGym([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var gymId = Id<LgymApi.Domain.Entities.Gym>.TryParse(id, out var parsedGymId) ? parsedGymId : Id<LgymApi.Domain.Entities.Gym>.Empty;

        var result = await _gymService.GetGymAsync(currentAccount, gymId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<LgymApi.Application.WorkoutProgress.Persistence.WorkoutGymPersistenceModel, GymFormDto>(result.Value));
    }

    [HttpPost("gym/editGym")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditGym([FromBody] GymFormDto form, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var gymId = Id<LgymApi.Domain.Entities.Gym>.TryParse(form.Id, out var parsedGymId) ? parsedGymId : Id<LgymApi.Domain.Entities.Gym>.Empty;

        var result = await _gymService.UpdateGymAsync(currentAccount, gymId, form.Name, form.Address, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
