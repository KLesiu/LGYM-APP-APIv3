using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Persistence;

public interface IReportTemplatePersistence
{
    Task AddAsync(NewReportTemplatePersistenceModel template, CancellationToken cancellationToken = default);
    Task<ReportTemplatePersistenceModel?> FindByIdAsync(Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReportTemplatePersistenceModel>> ListByTrainerAsync(Id<AccountReference> trainerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Id<ReportTemplate> templateId, UpdateReportTemplatePersistenceModel update, CancellationToken cancellationToken = default);
    Task MarkDeletedAsync(Id<ReportTemplate> templateId, CancellationToken cancellationToken = default);
}
