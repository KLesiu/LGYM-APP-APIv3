using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Trainer.Controllers;

[ApiController]
[Route("api/trainee")]
[Authorize]
public sealed class TraineeNotesController : ControllerBase
{
    private readonly ITraineeNotesApiPort _notes;
    private readonly IMapper _mapper;

    public TraineeNotesController(
        ITraineeNotesApiPort notes,
        IMapper mapper)
    {
        _notes = notes;
        _mapper = mapper;
    }

    [HttpGet("notes")]
    [ProducesResponseType(typeof(List<TraineeNoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisibleNotes(CancellationToken cancellationToken = default)
    {
        var result = await _notes.GetVisibleNotesAsync(HttpContext.GetAuthenticatedAccountContext()!, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.MapList<TraineeNoteReadModel, TraineeNoteDto>(result.Value));
    }

    [HttpGet("notes/{noteId}")]
    [ProducesResponseType(typeof(TraineeNoteDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisibleNote([FromRoute] string noteId, CancellationToken cancellationToken = default)
    {
        if (!LgymApi.Domain.ValueObjects.Id<TraineeNote>.TryParse(noteId, out var parsedNoteId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _notes.GetVisibleNoteAsync(HttpContext.GetAuthenticatedAccountContext()!, parsedNoteId, cancellationToken);
        return result.IsFailure ? result.ToActionResult() : Ok(_mapper.Map<TraineeNoteReadModel, TraineeNoteDto>(result.Value));
    }

    [HttpGet("notes/{noteId}/history")]
    [ProducesResponseType(typeof(List<TraineeNoteHistoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVisibleNoteHistory(
        [FromRoute] string noteId,
        CancellationToken cancellationToken = default)
    {
        if (!LgymApi.Domain.ValueObjects.Id<TraineeNote>.TryParse(noteId, out var parsedNoteId))
        {
            return BadRequest(_mapper.Map<string, ResponseMessageDto>(Messages.FieldRequired));
        }

        var result = await _notes.GetVisibleHistoryAsync(
            HttpContext.GetAuthenticatedAccountContext()!,
            parsedNoteId,
            cancellationToken);
        return result.IsFailure
            ? result.ToActionResult()
            : Ok(_mapper.MapList<TraineeNoteHistoryReadModel, TraineeNoteHistoryDto>(result.Value));
    }
}
