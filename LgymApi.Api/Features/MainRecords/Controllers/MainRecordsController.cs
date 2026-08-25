using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Enum;
using LgymApi.Api.Features.MainRecords.Contracts;
using LgymApi.Api.Extensions;
using LgymApi.Api.Middleware;
using LgymApi.Api.Mapping.Profiles;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ApiAdapters;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using Microsoft.AspNetCore.Mvc;
namespace LgymApi.Api.Features.MainRecords.Controllers;

[ApiController]
[Route("api")]
public sealed class MainRecordsController : ControllerBase
{
    private readonly IMainRecordsApiAdapter _mainRecordsService;
    private readonly IMapper _mapper;
    private readonly IExerciseApiAdapter _exercises;

    public MainRecordsController(
        IMainRecordsApiAdapter mainRecordsService,
        IMapper mapper,
        IExerciseApiAdapter exercises)
    {
        _mainRecordsService = mainRecordsService;
        _mapper = mapper;
        _exercises = exercises;
    }

    [HttpPost("mainRecords/{id}/addNewRecord")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddNewRecord([FromRoute] string id, [FromBody] MainRecordsFormDto form, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var exerciseId = form.ExerciseId.ToIdOrEmpty<Domain.Entities.Exercise>();
        var result = await _mainRecordsService.AddNewRecordAsync(accountId, exerciseId, form.Weight, form.Unit, form.Date, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Created));
    }

    [HttpGet("mainRecords/{id}/getMainRecordsHistory")]
    [ProducesResponseType(typeof(List<MainRecordResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMainRecordsHistory([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var result = await _mainRecordsService.GetMainRecordsHistoryAsync(accountId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var mappedRecords = _mapper.MapList<MainRecordReadModel, MainRecordResponseDto>(result.Value);
        return Ok(mappedRecords);
    }

    [HttpGet("mainRecords/{id}/getLastMainRecords")]
    [ProducesResponseType(typeof(List<MainRecordsLastDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    // Route name is legacy; payload contains best (max) record per exercise.
    public async Task<IActionResult> GetLastMainRecords([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var accountId = ParseRouteAccountIdForCurrentAccount(id);
        var result = await _mainRecordsService.GetLastMainRecordsAsync(accountId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var translations = await _exercises.GetDisplayNamesAsync(
            result.Value.Select(record => record.Exercise.Id),
            HttpContext.GetCulturePreferences(),
            cancellationToken);
        var mappingContext = _mapper.CreateContext();
        mappingContext.Set(ExerciseProfile.Keys.Translations, translations);
        var mapped = _mapper.MapList<MainRecordBestReadModel, MainRecordsLastDto>(
            result.Value,
            mappingContext);
        return Ok(mapped);
    }

    [HttpGet("mainRecords/{id}/deleteMainRecord")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMainRecord([FromRoute] string id, CancellationToken cancellationToken = default)
    {
        var currentAccountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        var recordId = id.ToIdOrEmpty<Domain.Entities.MainRecord>();
        var result = await _mainRecordsService.DeleteMainRecordAsync(currentAccountId, recordId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpPost("mainRecords/{id}/updateMainRecords")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMainRecords([FromRoute] string id, [FromBody] MainRecordsFormDto form, CancellationToken cancellationToken = default)
    {
        var routeAccountId = ParseRouteAccountIdForCurrentAccount(id);
        var recordId = form.Id.ToIdOrEmpty<Domain.Entities.MainRecord>();
        var exerciseId = form.ExerciseId.ToIdOrEmpty<Domain.Entities.Exercise>();
        var result = await _mainRecordsService.UpdateMainRecordAsync(routeAccountId, routeAccountId, recordId, exerciseId, form.Weight, form.Unit, form.Date, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Updated));
    }

    [HttpPost("mainRecords/getRecordOrPossibleRecordInExercise")]
    [ProducesResponseType(typeof(PossibleRecordForExerciseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecordOrPossibleRecordInExercise([FromBody] RecordOrPossibleRequestDto request, CancellationToken cancellationToken = default)
    {
        var accountId = HttpContext.GetAuthenticatedAccountContext()?.Id ?? Id<AccountReference>.Empty;
        var exerciseId = request.ExerciseId.ToIdOrEmpty<Domain.Entities.Exercise>();
        var result = await _mainRecordsService.GetRecordOrPossibleRecordInExerciseAsync(accountId, exerciseId, cancellationToken);
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<PossibleRecordReadModel, PossibleRecordForExerciseDto>(result.Value));
    }

    private Id<AccountReference> ParseRouteAccountIdForCurrentAccount(string routeAccountId)
    {
        var currentAccount = HttpContext.GetAuthenticatedAccountContext();
        if (currentAccount is null || currentAccount.Id.IsEmpty ||
            !Id<AccountReference>.TryParse(routeAccountId, out var parsedAccountId) ||
            parsedAccountId != currentAccount.Id)
        {
            throw new UnauthorizedAccessException(Messages.Forbidden);
        }

        return parsedAccountId;
    }
}
