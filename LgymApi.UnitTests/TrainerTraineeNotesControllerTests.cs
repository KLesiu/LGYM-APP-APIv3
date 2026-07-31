using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Trainer.Contracts;
using LgymApi.Api.Features.Trainer.Controllers;
using LgymApi.Api.Middleware;
using LgymApi.Application.Coaching.TraineeNotes.Create;
using LgymApi.Application.Coaching.TraineeNotes.Delete;
using LgymApi.Application.Coaching.TraineeNotes.History;
using LgymApi.Application.Coaching.TraineeNotes.Models;
using LgymApi.Application.Coaching.TraineeNotes.TrainerList;
using LgymApi.Application.Coaching.TraineeNotes.Update;
using LgymApi.Application.Coaching.ApiAdapters;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TrainerTraineeNotesControllerTests
{
    [Test]
    public async Task GetNotes_WithInvalidTraineeId_ForwardsEmptyIdToService()
    {
        var listNotes = Substitute.For<IListTrainerNotesUseCase>();
        Id<User> capturedId = Id<User>.New();
        listNotes.ExecuteAsync(Arg.Do<ListTrainerNotesQuery>(query => capturedId = query.TraineeId), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TraineeNoteReadModel>, AppError>([]));
        var controller = CreateController(listNotes: listNotes);

        var result = await controller.GetNotes("bad-id");

        result.Should().BeOfType<OkObjectResult>();
        capturedId.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task CreateNote_WithValidRequest_MapsCommandAndReturnsCreated()
    {
        var createNote = Substitute.For<ICreateTraineeNoteUseCase>();
        var traineeId = Id<User>.New();
        CreateTraineeNoteCommand? captured = null;
        createNote.ExecuteAsync(Arg.Do<CreateTraineeNoteCommand>(command => captured = command), Arg.Any<CancellationToken>())
            .Returns(Result.Success<TraineeNoteReadModel, AppError>(CreateNoteResult()));
        var controller = CreateController(createNote: createNote);

        var result = await controller.CreateNote(traineeId.ToString(), new UpsertTraineeNoteRequest
        {
            Title = "Plan",
            Content = "Warm up first",
            VisibleToTrainee = true,
            IsPinned = true
        });

        result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)result).StatusCode.Should().Be(StatusCodes.Status201Created);
        captured.Should().NotBeNull();
        captured.Should().NotBeNull();
        captured!.Data.Content.Should().Be("Warm up first");
    }

    [Test]
    public async Task UpdateNote_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController();

        (await controller.UpdateNote("bad-user", Id<TraineeNote>.New().ToString(), new UpsertTraineeNoteRequest())).Should().BeAssignableTo<ObjectResult>();
        (await controller.UpdateNote(Id<User>.New().ToString(), "bad-note", new UpsertTraineeNoteRequest())).Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task UpdateNote_WithValidIds_ReturnsMappedDto()
    {
        var updateNote = Substitute.For<IUpdateTraineeNoteUseCase>();
        var traineeId = Id<User>.New();
        var noteId = Id<TraineeNote>.New();
        updateNote.ExecuteAsync(Arg.Any<UpdateTraineeNoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<TraineeNoteReadModel, AppError>(CreateNoteResult()));
        var controller = CreateController(updateNote: updateNote);

        var result = await controller.UpdateNote(traineeId.ToString(), noteId.ToString(), new UpsertTraineeNoteRequest { Content = "Updated" });

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task DeleteNote_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController();

        (await controller.DeleteNote("bad-user", Id<TraineeNote>.New().ToString())).Should().BeAssignableTo<ObjectResult>();
        (await controller.DeleteNote(Id<User>.New().ToString(), "bad-note")).Should().BeAssignableTo<ObjectResult>();
    }

    [Test]
    public async Task DeleteNote_WithValidIds_ReturnsDeletedMessage()
    {
        var deleteNote = Substitute.For<IDeleteTraineeNoteUseCase>();
        var traineeId = Id<User>.New();
        var noteId = Id<TraineeNote>.New();
        deleteNote.ExecuteAsync(Arg.Any<DeleteTraineeNoteCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<Unit, AppError>(Unit.Value));
        var controller = CreateController(deleteNote: deleteNote);

        var result = await controller.DeleteNote(traineeId.ToString(), noteId.ToString());

        result.Should().BeOfType<OkObjectResult>();
    }

    [Test]
    public async Task GetNoteHistory_WithValidIds_ReturnsMappedDtos()
    {
        var history = Substitute.For<IGetTraineeNoteHistoryUseCase>();
        var traineeId = Id<User>.New();
        var noteId = Id<TraineeNote>.New();
        history.ExecuteAsync(Arg.Any<GetTraineeNoteHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TraineeNoteHistoryReadModel>, AppError>([new TraineeNoteHistoryReadModel(
                Id<TraineeNoteHistory>.New(),
                noteId,
                Id<User>.New(),
                DateTimeOffset.UtcNow,
                null,
                "Updated",
                "Update")]));
        var controller = CreateController(history: history);

        var result = await controller.GetNoteHistory(traineeId.ToString(), noteId.ToString());

        result.Should().BeOfType<OkObjectResult>();
        ((OkObjectResult)result).Value.Should().BeAssignableTo<IEnumerable<TraineeNoteHistoryDto>>();
    }

    [Test]
    public async Task GetNoteHistory_WithInvalidIds_ReturnsBadRequest()
    {
        var controller = CreateController();

        (await controller.GetNoteHistory("bad-user", Id<TraineeNote>.New().ToString())).Should().BeAssignableTo<ObjectResult>();
        (await controller.GetNoteHistory(Id<User>.New().ToString(), "bad-note")).Should().BeAssignableTo<ObjectResult>();
    }

    private static TrainerTraineeNotesController CreateController(
        IListTrainerNotesUseCase? listNotes = null,
        ICreateTraineeNoteUseCase? createNote = null,
        IUpdateTraineeNoteUseCase? updateNote = null,
        IDeleteTraineeNoteUseCase? deleteNote = null,
        IGetTraineeNoteHistoryUseCase? history = null)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        var controller = new TrainerTraineeNotesController(
            new TrainerTraineeNotesApiAdapter(listNotes ?? Substitute.For<IListTrainerNotesUseCase>(), createNote ?? Substitute.For<ICreateTraineeNoteUseCase>(), updateNote ?? Substitute.For<IUpdateTraineeNoteUseCase>(), deleteNote ?? Substitute.For<IDeleteTraineeNoteUseCase>(), history ?? Substitute.For<IGetTraineeNoteHistoryUseCase>(), mapper),
            mapper)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Features.Set<IAuthenticatedAccountContextFeature>(new AuthenticatedAccountContextFeature(
            new AuthenticatedAccountContext(Id<AccountReference>.New(), null, [], [], false, false)));
        return controller;
    }

    private static TraineeNoteReadModel CreateNoteResult()
        => new(
            Id<TraineeNote>.New(),
            Id<User>.New(),
            Id<User>.New(),
            "Reminder",
            "Warm up first",
            true,
            true,
            Id<User>.New(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
}
