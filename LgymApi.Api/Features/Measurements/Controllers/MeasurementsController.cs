using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Measurements.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Task7ApiCompatibility.WorkoutProgress;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Security;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Measurements.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class MeasurementsController : ControllerBase
{
    private readonly IMeasurementsApiCompatibilityService _measurementsService;
    private readonly IMapper _mapper;

    public MeasurementsController(IMeasurementsApiCompatibilityService measurementsService, IMapper mapper)
    {
        _measurementsService = measurementsService;
        _mapper = mapper;
    }

    [HttpPost("measurements/add")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMeasurement([FromBody] MeasurementFormDto form, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var result = await _measurementsService.AddMeasurementAsync(currentAccount, form.BodyPart, form.Unit, form.Value, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpPost("measurements/add-bulk")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddMeasurementsBulk([FromBody] MeasurementsBulkFormDto form, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var inputs = form.Measurements
            .Select(item => new MeasurementCreateInput
            {
                BodyPart = item.BodyPart,
                Unit = item.Unit,
                Value = item.Value
            })
            .ToList();

        var result = await _measurementsService.AddMeasurementsAsync(currentAccount, inputs, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpGet("measurements:/{id}/getMeasurementDetail")]
    [ProducesResponseType(typeof(MeasurementResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeasurementDetail([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        var measurementId = Id<LgymApi.Domain.Entities.Measurement>.TryParse(id, out var parsedId) ? parsedId : Id<LgymApi.Domain.Entities.Measurement>.Empty;
        var result = await _measurementsService.GetMeasurementDetailAsync(currentAccount, measurementId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<MeasurementReadModel, MeasurementResponseDto>(result.Value));
    }

    [HttpGet("measurements/{id}/getHistory")]
    [ProducesResponseType(typeof(MeasurementsHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeasurementsHistory([FromRoute] string id, [FromQuery] MeasurementsHistoryRequestDto? request, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        Id<AccountReference>.TryParse(id, out var parsedAccountId);
        var result = await _measurementsService.GetMeasurementsHistoryAsync(currentAccount, parsedAccountId, request?.BodyPart, request?.Unit, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var dto = _mapper.Map<List<MeasurementReadModel>, MeasurementsHistoryDto>(result.Value);
        return Ok(dto);
    }

    [HttpGet("measurements/{id}/list")]
    [ProducesResponseType(typeof(MeasurementsListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeasurementsList([FromRoute] string id, [FromQuery] MeasurementsHistoryRequestDto? request, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        Id<AccountReference>.TryParse(id, out var parsedAccountId);
        var result = await _measurementsService.GetMeasurementsListAsync(currentAccount, parsedAccountId, request?.BodyPart, request?.Unit, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var dto = _mapper.Map<List<MeasurementReadModel>, MeasurementsListDto>(result.Value);
        return Ok(dto);
    }

    [HttpGet("measurements/{id}/trend")]
    [ProducesResponseType(typeof(MeasurementTrendDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeasurementsTrend([FromRoute] string id, [FromQuery] MeasurementTrendRequestDto request, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        Id<AccountReference>.TryParse(id, out var parsedAccountId);
        var result = await _measurementsService.GetMeasurementsTrendAsync(currentAccount, parsedAccountId, request.BodyPart, request.Unit, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<MeasurementTrendReadModel, MeasurementTrendDto>(result.Value));
    }

    [HttpGet("measurements/{id}/trends")]
    [ProducesResponseType(typeof(MeasurementTrendsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMeasurementsTrends([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        Id<AccountReference>.TryParse(id, out var parsedAccountId);
        var result = await _measurementsService.GetMeasurementsTrendsAsync(currentAccount, parsedAccountId, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<List<MeasurementTrendReadModel>, MeasurementTrendsDto>(result.Value));
    }

}
