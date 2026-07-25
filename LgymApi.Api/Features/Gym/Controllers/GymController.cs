using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Gym.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.Features.Gym;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.ValueObjects;
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
        var user = HttpContext.GetCurrentUser();
        var routeUserId = Id<LgymApi.Domain.Entities.User>.TryParse(id, out var parsedUserId) ? parsedUserId : Id<LgymApi.Domain.Entities.User>.Empty;

        var result = await _gymService.AddGymAsync(user!, routeUserId, form.Name, form.Address, cancellationToken);
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
        var user = HttpContext.GetCurrentUser();
        var gymId = Id<LgymApi.Domain.Entities.Gym>.TryParse(id, out var parsedGymId) ? parsedGymId : Id<LgymApi.Domain.Entities.Gym>.Empty;

        var result = await _gymService.DeleteGymAsync(user!, gymId, cancellationToken);
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
        var user = HttpContext.GetCurrentUser();
        var routeUserId = Id<LgymApi.Domain.Entities.User>.TryParse(id, out var parsedUserId) ? parsedUserId : Id<LgymApi.Domain.Entities.User>.Empty;

        var result = await _gymService.GetGymsAsync(user!, routeUserId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var context = result.Value;
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(GymProfile.Keys.LastTrainingMap, context.LastTrainings);

        var gyms = _mapper.MapList<LgymApi.Domain.Entities.Gym, GymChoiceInfoDto>(context.Gyms, mappingContext);

        return Ok(gyms);
    }

    [HttpGet("gym/{id}/getGym")]
    [ProducesResponseType(typeof(GymFormDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGym([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        var gymId = Id<LgymApi.Domain.Entities.Gym>.TryParse(id, out var parsedGymId) ? parsedGymId : Id<LgymApi.Domain.Entities.Gym>.Empty;

        var result = await _gymService.GetGymAsync(user!, gymId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<LgymApi.Domain.Entities.Gym, GymFormDto>(result.Value));
    }

    [HttpPost("gym/editGym")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EditGym([FromBody] GymFormDto form, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.GetCurrentUser();
        var gymId = Id<LgymApi.Domain.Entities.Gym>.TryParse(form.Id, out var parsedGymId) ? parsedGymId : Id<LgymApi.Domain.Entities.Gym>.Empty;

        var result = await _gymService.UpdateGymAsync(user!, gymId, form.Name, form.Address, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }
}
