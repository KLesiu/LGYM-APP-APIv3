using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.Compatibility;
using LgymApi.Application.Coaching.TraineeNotes.Models;
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
public sealed class TrainerTraineeNotesController : ControllerBase
{
    private readonly ITrainerTraineeNotesApiPort _notes;
    private readonly IMapper _mapper;

    public TrainerTraineeNotesController(
        ITrainerTraineeNotesApiPort notes,
        IMapper mapper)
    {
        _notes = notes;
        _mapper = mapper;
    }

    [HttpGet("trainees/{traineeId}/notes")]
    [ProducesResponseType(typeof(List<TraineeNoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotes([FromRoute] string traineeId, CancellationToken cancellationToken = default)
    {
        Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId);
        var result = await _notes.GetNotesAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<TraineeNoteReadModel, TraineeNoteDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/notes")]
    [ProducesResponseType(typeof(TraineeNoteDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateNote([FromRoute] string traineeId, [FromBody] UpsertTraineeNoteRequest request, CancellationToken cancellationToken = default)
    {
        Id<AccountReference>.TryParse(traineeId, out var parsedTraineeId);
        var result = await _notes.CreateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, new TraineeNoteUpsertData(request.Title, request.Content, request.VisibleToTrainee, request.IsPinned), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : StatusCode(StatusCodes.Status201Created, _mapper.Map<TraineeNoteReadModel, TraineeNoteDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/notes/{noteId}/update")]
    [ProducesResponseType(typeof(TraineeNoteDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateNote([FromRoute] string traineeId, [FromRoute] string noteId, [FromBody] UpsertTraineeNoteRequest request, CancellationToken cancellationToken = default)
    {
        if (!TryParseIds(traineeId, noteId, out var parsedTraineeId, out var parsedNoteId, out var errorResult))
        {
            return errorResult!;
        }

        var result = await _notes.UpdateAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedNoteId, new TraineeNoteUpsertData(request.Title, request.Content, request.VisibleToTrainee, request.IsPinned), cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<TraineeNoteReadModel, TraineeNoteDto>(result.Value));
    }

    [HttpPost("trainees/{traineeId}/notes/{noteId}/delete")]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteNote([FromRoute] string traineeId, [FromRoute] string noteId, CancellationToken cancellationToken = default)
    {
        if (!TryParseIds(traineeId, noteId, out var parsedTraineeId, out var parsedNoteId, out var errorResult))
        {
            return errorResult!;
        }

        var result = await _notes.DeleteAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedNoteId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<string, ResponseMessageDto>(Messages.Deleted));
    }

    [HttpGet("trainees/{traineeId}/notes/{noteId}/history")]
    [ProducesResponseType(typeof(List<TraineeNoteHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNoteHistory([FromRoute] string traineeId, [FromRoute] string noteId, CancellationToken cancellationToken = default)
    {
        if (!TryParseIds(traineeId, noteId, out var parsedTraineeId, out var parsedNoteId, out var errorResult))
        {
            return errorResult!;
        }

        var result = await _notes.GetHistoryAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedTraineeId, parsedNoteId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<TraineeNoteHistoryReadModel, TraineeNoteHistoryDto>(result.Value));
    }

    private bool TryParseIds(string traineeId, string noteId, out Id<AccountReference> parsedTraineeId, out Id<TraineeNote> parsedNoteId, out IActionResult? errorResult)
    {
        errorResult = null;
        if (!Id<AccountReference>.TryParse(traineeId, out parsedTraineeId))
        {
            parsedNoteId = default;
            errorResult = BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.UserIdRequired));
            return false;
        }

        if (!Id<TraineeNote>.TryParse(noteId, out parsedNoteId))
        {
            errorResult = BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
            return false;
        }

        return true;
    }
}
