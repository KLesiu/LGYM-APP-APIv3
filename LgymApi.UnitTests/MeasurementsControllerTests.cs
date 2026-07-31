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
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Identity.Contracts.Accounts;
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

        service.LastMeasurementId.Should().Be(Id<LgymApi.Domain.Entities.Measurement>.Empty);
    }

    [Test]
    public async Task GetMeasurementsTrend_WithInvalidRouteId_UsesEmptyRouteAccountId()
    {
        var service = new StubMeasurementsService();
        var controller = CreateController(service);

        await controller.GetMeasurementsTrend("invalid", new MeasurementTrendRequestDto
        {
            BodyPart = BodyParts.BodyWeight,
            Unit = MeasurementUnits.Kilograms
        });

        service.LastRouteAccountId.Should().Be(Id<AccountReference>.Empty);
    }

    [Test]
    public async Task GetMeasurementsTrends_WithInvalidRouteId_UsesEmptyRouteAccountId()
    {
        var service = new StubMeasurementsService();
        var controller = CreateController(service);

        await controller.GetMeasurementsTrends("invalid");

        service.LastRouteAccountId.Should().Be(Id<AccountReference>.Empty);
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
        service.LastCurrentAccount?.Id.Should().NotBe(Id<AccountReference>.Empty);
        service.LastBulkMeasurements[0].BodyPart.Should().Be(BodyParts.BodyWeight);
        service.LastBulkMeasurements[1].BodyPart.Should().Be(BodyParts.Waist);
    }

    private static MeasurementsController CreateController(StubMeasurementsService service)
    {
        var services = new ServiceCollection();
        services.AddApplicationMapping(LgymApi.Api.Mapping.MappingAssemblyMarkers.All);
        using var provider = services.BuildServiceProvider();
        var mapper = provider.GetRequiredService<IMapper>();
        return new MeasurementsController(service, mapper)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Items =
                    {
                        ["AuthenticatedAccountContext"] = new AuthenticatedAccountContext(
                            Id<AccountReference>.New(),
                            null,
                            [],
                            [],
                            false,
                            false)
                    }
                }
            }
        };
    }

    private sealed class StubMeasurementsService : IMeasurementsService
    {
        public Id<LgymApi.Domain.Entities.Measurement> LastMeasurementId { get; private set; } = Id<LgymApi.Domain.Entities.Measurement>.Empty;
        public Id<AccountReference> LastRouteAccountId { get; private set; } = Id<AccountReference>.Empty;
        public List<MeasurementCreateInput> LastBulkMeasurements { get; private set; } = new();
        public AuthenticatedAccountContext? LastCurrentAccount { get; private set; }

        public Task<Result<Unit, AppError>> AddMeasurementAsync(AuthenticatedAccountContext? currentAccount, BodyParts bodyPart, MeasurementUnits unit, double value, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));

        public Task<Result<Unit, AppError>> AddMeasurementsAsync(AuthenticatedAccountContext? currentAccount, IReadOnlyCollection<MeasurementCreateInput> measurements, CancellationToken cancellationToken = default)
        {
            LastCurrentAccount = currentAccount;
            LastBulkMeasurements = measurements.ToList();
            return Task.FromResult(Result<Unit, AppError>.Success(Unit.Value));
        }

        public Task<Result<MeasurementReadModel, AppError>> GetMeasurementDetailAsync(AuthenticatedAccountContext? currentAccount, Id<LgymApi.Domain.Entities.Measurement> measurementId, CancellationToken cancellationToken = default)
        {
            LastMeasurementId = measurementId;
            return Task.FromResult(Result<MeasurementReadModel, AppError>.Failure(new BadRequestError("Invalid measurement id.")));
        }

        public Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsListAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
        {
            LastRouteAccountId = routeAccountId;
            return Task.FromResult(Result<List<MeasurementReadModel>, AppError>.Success([]));
        }

        public Task<Result<List<MeasurementReadModel>, AppError>> GetMeasurementsHistoryAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts? bodyPart, MeasurementUnits? unit, CancellationToken cancellationToken = default)
        {
            LastRouteAccountId = routeAccountId;
            return Task.FromResult(Result<List<MeasurementReadModel>, AppError>.Success([]));
        }

        public Task<Result<MeasurementTrendReadModel, AppError>> GetMeasurementsTrendAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, BodyParts bodyPart, MeasurementUnits unit, CancellationToken cancellationToken = default)
        {
            LastRouteAccountId = routeAccountId;
            return Task.FromResult(Result<MeasurementTrendReadModel, AppError>.Success(new(bodyPart, unit, null, null, null, null, null, null, null, null, null, "same", 2)));
        }

        public Task<Result<List<MeasurementTrendReadModel>, AppError>> GetMeasurementsTrendsAsync(AuthenticatedAccountContext? currentAccount, Id<AccountReference> routeAccountId, CancellationToken cancellationToken = default)
        {
            LastRouteAccountId = routeAccountId;
            return Task.FromResult(Result<List<MeasurementTrendReadModel>, AppError>.Success([]));
        }
    }
}
