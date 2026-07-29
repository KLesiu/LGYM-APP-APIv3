using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Options;
using LgymApi.Application.Repositories;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using Microsoft.Extensions.Logging;

namespace LgymApi.Application.Features.Reporting;

public interface IReportingServiceDependencies
{
    IReportTemplatePersistence TemplatePersistence { get; }
    IReportRequestSubmissionPersistence RequestSubmissionPersistence { get; }
    IRecurringReportAssignmentPersistence RecurringAssignmentPersistence { get; }
    IReportPhotoPersistence PhotoPersistence { get; }
    IReportingRelationshipAccessPersistence RelationshipAccessPersistence { get; }
    IReportSubmissionAcceptedProgressCommandFactory ReportSubmissionAcceptedProgressCommandFactory { get; }
    ICommandDispatcher CommandDispatcher { get; }
    ICommandOutboxWriter CommandOutboxWriter { get; }
    IUnitOfWork UnitOfWork { get; }
    IPhotoStorageProvider PhotoStorageProvider { get; }
    IMapper Mapper { get; }
    ILogger<ReportingService> Logger { get; }
    PhotoStorageOptions PhotoStorageOptions { get; }
}

internal sealed class ReportingServiceDependencies : IReportingServiceDependencies
{
    public ReportingServiceDependencies(
        IReportTemplatePersistence templatePersistence,
        IReportRequestSubmissionPersistence requestSubmissionPersistence,
        IRecurringReportAssignmentPersistence recurringAssignmentPersistence,
        IReportPhotoPersistence photoPersistence,
        IReportingRelationshipAccessPersistence relationshipAccessPersistence,
        IReportSubmissionAcceptedProgressCommandFactory reportSubmissionAcceptedProgressCommandFactory,
        ICommandDispatcher commandDispatcher,
        ICommandOutboxWriter commandOutboxWriter,
        IUnitOfWork unitOfWork,
        IPhotoStorageProvider photoStorageProvider,
        IMapper mapper,
        ILogger<ReportingService> logger,
        PhotoStorageOptions photoStorageOptions)
    {
        TemplatePersistence = templatePersistence;
        RequestSubmissionPersistence = requestSubmissionPersistence;
        RecurringAssignmentPersistence = recurringAssignmentPersistence;
        PhotoPersistence = photoPersistence;
        RelationshipAccessPersistence = relationshipAccessPersistence;
        ReportSubmissionAcceptedProgressCommandFactory = reportSubmissionAcceptedProgressCommandFactory;
        CommandDispatcher = commandDispatcher;
        CommandOutboxWriter = commandOutboxWriter;
        UnitOfWork = unitOfWork;
        PhotoStorageProvider = photoStorageProvider;
        Mapper = mapper;
        Logger = logger;
        PhotoStorageOptions = photoStorageOptions;
    }

    public IReportTemplatePersistence TemplatePersistence { get; }
    public IReportRequestSubmissionPersistence RequestSubmissionPersistence { get; }
    public IRecurringReportAssignmentPersistence RecurringAssignmentPersistence { get; }
    public IReportPhotoPersistence PhotoPersistence { get; }
    public IReportingRelationshipAccessPersistence RelationshipAccessPersistence { get; }
    public IReportSubmissionAcceptedProgressCommandFactory ReportSubmissionAcceptedProgressCommandFactory { get; }
    public ICommandDispatcher CommandDispatcher { get; }
    public ICommandOutboxWriter CommandOutboxWriter { get; }
    public IUnitOfWork UnitOfWork { get; }
    public IPhotoStorageProvider PhotoStorageProvider { get; }
    public IMapper Mapper { get; }
    public ILogger<ReportingService> Logger { get; }
    public PhotoStorageOptions PhotoStorageOptions { get; }
}
