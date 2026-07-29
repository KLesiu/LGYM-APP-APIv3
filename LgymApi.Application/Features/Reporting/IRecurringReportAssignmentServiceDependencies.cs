using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Repositories;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;

namespace LgymApi.Application.Features.Reporting;

public interface IRecurringReportAssignmentServiceDependencies
{
    IReportTemplatePersistence TemplatePersistence { get; }
    IReportRequestSubmissionPersistence RequestSubmissionPersistence { get; }
    IRecurringReportAssignmentPersistence RecurringAssignmentPersistence { get; }
    IReportingRelationshipAccessPersistence RelationshipAccessPersistence { get; }
    IMapper Mapper { get; }
    ICommandDispatcher CommandDispatcher { get; }
    IUnitOfWork UnitOfWork { get; }
}

internal sealed class RecurringReportAssignmentServiceDependencies : IRecurringReportAssignmentServiceDependencies
{
    public RecurringReportAssignmentServiceDependencies(
        IReportTemplatePersistence templatePersistence,
        IReportRequestSubmissionPersistence requestSubmissionPersistence,
        IRecurringReportAssignmentPersistence recurringAssignmentPersistence,
        IReportingRelationshipAccessPersistence relationshipAccessPersistence,
        IMapper mapper,
        ICommandDispatcher commandDispatcher,
        IUnitOfWork unitOfWork)
    {
        TemplatePersistence = templatePersistence;
        RequestSubmissionPersistence = requestSubmissionPersistence;
        RecurringAssignmentPersistence = recurringAssignmentPersistence;
        RelationshipAccessPersistence = relationshipAccessPersistence;
        Mapper = mapper;
        CommandDispatcher = commandDispatcher;
        UnitOfWork = unitOfWork;
    }

    public IReportTemplatePersistence TemplatePersistence { get; }
    public IReportRequestSubmissionPersistence RequestSubmissionPersistence { get; }
    public IRecurringReportAssignmentPersistence RecurringAssignmentPersistence { get; }
    public IReportingRelationshipAccessPersistence RelationshipAccessPersistence { get; }
    public IMapper Mapper { get; }
    public ICommandDispatcher CommandDispatcher { get; }
    public IUnitOfWork UnitOfWork { get; }
}
