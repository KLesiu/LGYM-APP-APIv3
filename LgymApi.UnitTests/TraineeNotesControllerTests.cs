using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.VisibleList;
using LgymApi.Application.Coaching.TraineeNotes.VisibleSingle;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TraineeNotesControllerTests
{
    [Test]
    public async Task GetVisibleNotes_WhenServiceSucceeds_ReturnsMappedDtos()
    {
        var listNotes = Substitute.For<IListVisibleTraineeNotesUseCase>();
        listNotes.ExecuteAsync(Arg.Any<ListVisibleTraineeNotesQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TraineeNoteReadModel>, AppError>([CreateNoteResult()]));
        var controller = CreateController(listNotes: listNotes);

        var result = await controller.GetVisibleNotes();

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<TraineeNoteDto>>();
    }

    [Test]
    public async Task GetVisibleNote_WithInvalidId_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.GetVisibleNote("bad-id");

        result.Should().BeAssignableTo<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task GetVisibleNote_WhenServiceSucceeds_ReturnsMappedDto()
    {
        var getNote = Substitute.For<IGetVisibleTraineeNoteUseCase>();
        var noteId = Id<TraineeNote>.New();
        getNote.ExecuteAsync(Arg.Any<GetVisibleTraineeNoteQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<TraineeNoteReadModel, AppError>(CreateNoteResult(noteId)));
        var controller = CreateController(getNote: getNote);

        var result = await controller.GetVisibleNote(noteId.ToString());

        result.Should().BeOfType<OkObjectResult>();
    }

    private static TraineeNotesController CreateController(
        IListVisibleTraineeNotesUseCase? listNotes = null,
        IGetVisibleTraineeNoteUseCase? getNote = null)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(Program).Assembly, typeof(IMappingProfile).Assembly);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var controller = new TraineeNotesController(
            listNotes ?? Substitute.For<IListVisibleTraineeNotesUseCase>(),
            getNote ?? Substitute.For<IGetVisibleTraineeNoteUseCase>(),
            mapper)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Items["User"] = new User { Id = Id<User>.New(), Name = "Trainee", Email = "trainee@notes.test", ProfileRank = "Rookie" };
        return controller;
    }

    private static TraineeNoteReadModel CreateNoteResult(Id<TraineeNote>? noteId = null)
        => new(
            noteId ?? Id<TraineeNote>.New(),
            Id<User>.New(),
            Id<User>.New(),
            "Reminder",
            "Stay consistent",
            true,
            true,
            Id<User>.New(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
}
