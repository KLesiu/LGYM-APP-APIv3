using FluentAssertions;
using LgymApi.Api;
using LgymApi.Api.Features.Measurements.Controllers;
using LgymApi.Api.Features.Measurements.Contracts;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Features.Measurements.Models;
using LgymApi.Application.Mapping;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.WorkoutProgress.ProgressData.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class MeasurementsControllerTests
{
    [Test]
    public async Task GetMeasurementDetail_WithInvalidId_UsesEmptyTypedId()
    {
        var service = new StubMeasurementsService();
        var controller = CreateController(service);

        await controller.GetMeasurementDetail("not-a-guid");

        service.LastMeasurementId.Should().Be(Id<Measurement>.Empty);
    }

    [Test]
    public async Task GetMeasurementsTrend_WithInvalidRouteId_UsesEmptyRouteUserId()
    {
        var service = new StubMeasurementsService();
        var controller = CreateController(service);

        await controller.GetMeasurementsTrend("invalid", new MeasurementTrendRequestDto
        {
            BodyPart = BodyParts.BodyWeight,
            Unit = MeasurementUnits.Kilograms
        });

        service.LastRouteUserId.Should().Be(Id<User>.Empty);
    }

    [Test]
    public async Task GetMeasurementsTrends_WithInvalidRouteId_UsesEmptyRouteUserId()
    {
        var service = new StubMeasurementsService();
        var controller = CreateController(service);

        await controller.GetMeasurementsTrends("invalid");

        service.LastRouteUserId.Should().Be(Id<User>.Empty);
    }

    [Test]
    public async Task AddMeasurementsBulk_ForwardsAllMeasurementsToService()
    {
        var service = new StubMeasurementsService();
        var controller = CreateController(service);

        await controller.AddMeasurementsBulk(new MeasurementsBulkFormDto
        {
            Measurements =
            [
                new MeasurementFormDto { BodyPart = BodyParts.BodyWeight, Unit = MeasurementUnits.Kilograms, Value = 80 },
                new MeasurementFormDto { BodyPart = BodyParts.Waist, Unit = MeasurementUnits.Centimeters, Value = 90 }
            ]
        });

        service.LastBulkMeasurements.Should().HaveCount(2);
        service.LastBulkMeasurements[0].BodyPart.Should().Be(BodyParts.BodyWeight);
        service.LastBulkMeasurements[1].BodyPart.Should().Be(BodyParts.Waist);
    }

    private static MeasurementsController CreateController(StubMeasurementsService service)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(typeof(Program).Assembly, typeof(IMappingProfile).Assembly);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        return new MeasurementsController(service, mapper)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class StubMeasurementsService : IMeasurementsService
    {
        public Id<Measurement> LastMeasurementId { get; private set; } = Id<Measurement>.Empty;
        public Id<User> LastRouteUserId { get; private set; } = Id<User>.Empty;
        public List<MeasurementCreateInput> LastBulkMeasurements { get; private set; } = new();

        public Task<Result<Unit, AppError>> AddMeasurementAsync(User currentUser, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));

        public Task<Result<Unit, AppError>> AddMeasurementsAsync(User currentUser, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default)
        {
            LastBulkMeasurements = measurements.ToList();
            return Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));
        }

        public Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(User currentUser, Id<Measurement> measurementId, CancellationToken cancellationToken = default)
        {
            LastMeasurementId = measurementId;
            return Task.FromResult(Result<MeasurementReadModel, AppError>.Success(new(
                Id<Measurement>.New(), Id<User>.New(), BodyParts.Chest, MeasurementUnits.Centimeters, 10, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)));
        }

        public Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(User currentUser, Id<User> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
        {
            LastRouteUserId = routeUserId;
            return Task.FromResult(Result<List<MeasurementReadModel>, AppError>.Success([]));
        }

        public Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(User currentUser, Id<User> routeUserId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
        {
            LastRouteUserId = routeUserId;
            return Task.FromResult(Result<List<MeasurementReadModel>, AppError>.Success([]));
        }

        public Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(User currentUser, Id<User> routeUserId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default)
        {
            LastRouteUserId = routeUserId;
            return Task.FromResult(Result<MeasurementTrendReadModel, AppError>.Success(new(bodyPart, unit, null, null, null, null, null, null, null, null, null, "same", 2)));
        }

        public Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(User currentUser, Id<User> routeUserId, CancellationToken cancellationToken = default)
        {
            LastRouteUserId = routeUserId;
            return Task.FromResult(Result<List<MeasurementTrendReadModel>, AppError>.Success([]));
        }
    }
}
