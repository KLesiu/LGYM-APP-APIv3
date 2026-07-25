using LgymApi.Application.Repositories;
using LgymApi.Application.Identity.Contracts.Access;
using LgymApi.Application.Platform.ReferenceData.Units;
using LgymApi.Application.WorkoutProgress.Contracts.Measurements;
using LgymApi.Domain.Enums;

namespace LgymApi.Application.Features.Measurements;

public interface IMeasurementsServiceDependencies
{
    IMeasurementRepository MeasurementRepository { get; }
    IUserAccessReadService UserAccessReadService { get; }
    IMeasurementsRelationshipAccessPort MeasurementsRelationshipAccessPort { get; }
    IUnitConverter<HeightUnits> HeightUnitConverter { get; }
    IUnitConverter<WeightUnits> WeightUnitConverter { get; }
    IUnitOfWork UnitOfWork { get; }
}

internal sealed class MeasurementsServiceDependencies : IMeasurementsServiceDependencies
{
    public MeasurementsServiceDependencies(
        IMeasurementRepository measurementRepository,
        IUserAccessReadService userAccessReadService,
        IMeasurementsRelationshipAccessPort measurementsRelationshipAccessPort,
        IUnitConverter<HeightUnits> heightUnitConverter,
        IUnitConverter<WeightUnits> weightUnitConverter,
        IUnitOfWork unitOfWork)
    {
        MeasurementRepository = measurementRepository;
        UserAccessReadService = userAccessReadService;
        MeasurementsRelationshipAccessPort = measurementsRelationshipAccessPort;
        HeightUnitConverter = heightUnitConverter;
        WeightUnitConverter = weightUnitConverter;
        UnitOfWork = unitOfWork;
    }

    public IMeasurementRepository MeasurementRepository { get; }
    public IUserAccessReadService UserAccessReadService { get; }
    public IMeasurementsRelationshipAccessPort MeasurementsRelationshipAccessPort { get; }
    public IUnitConverter<HeightUnits> HeightUnitConverter { get; }
    public IUnitConverter<WeightUnits> WeightUnitConverter { get; }
    public IUnitOfWork UnitOfWork { get; }
}
