using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.Infrastructure.Repositories.Reporting;

public sealed class ReportTemplatePersistenceRepository : IReportTemplatePersistence
{
    private readonly AppDbContext _dbContext;

    public ReportTemplatePersistenceRepository(AppDbContext dbContext) => _dbContext = dbContext;

    public Task AddAsync(NewReportTemplatePersistenceModel template, CancellationToken cancellationToken = default)
        => _dbContext.ReportTemplates.AddAsync(CreateEntity(template), cancellationToken).AsTask();

    public async Task<ReportTemplatePersistenceModel?> FindByIdAsync(
        Id<ReportTemplate> templateId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ReportTemplates
            .AsNoTracking()
            .Include(template => template.Fields.OrderBy(field => field.Order).ThenBy(field => field.CreatedAt))
            .FirstOrDefaultAsync(template => template.Id == templateId, cancellationToken);
        return entity is null ? null : ReportingPersistenceProjection.Template(entity);
    }

    public async Task<IReadOnlyList<ReportTemplatePersistenceModel>> ListByTrainerAsync(
        Id<AccountReference> trainerId,
        CancellationToken cancellationToken = default)
    {
        var persistedTrainerId = ReportingPersistenceAccountIds.ToPersisted(trainerId);
        var entities = await _dbContext.ReportTemplates
            .AsNoTracking()
            .Where(template => template.TrainerId == persistedTrainerId && !template.IsDeleted)
            .Include(template => template.Fields.OrderBy(field => field.Order).ThenBy(field => field.CreatedAt))
            .OrderByDescending(template => template.CreatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(ReportingPersistenceProjection.Template).ToList();
    }

    public async Task UpdateAsync(
        Id<ReportTemplate> templateId,
        UpdateReportTemplatePersistenceModel update,
        CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ReportTemplates
            .Include(template => template.Fields)
            .FirstAsync(template => template.Id == templateId, cancellationToken);
        entity.Name = update.Name;
        entity.Description = update.Description;
        entity.Fields.Clear();
        foreach (var field in update.Fields)
        {
            entity.Fields.Add(CreateFieldEntity(templateId, field));
        }
    }

    public async Task MarkDeletedAsync(Id<ReportTemplate> templateId, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.ReportTemplates.FirstAsync(template => template.Id == templateId, cancellationToken);
        entity.IsDeleted = true;
    }

    private static ReportTemplate CreateEntity(NewReportTemplatePersistenceModel template)
        => new()
        {
            Id = template.Id,
            TrainerId = ReportingPersistenceAccountIds.ToPersisted(template.TrainerId),
            Name = template.Name,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            Fields = template.Fields.Select(field => CreateFieldEntity(template.Id, field)).ToList()
        };

    private static ReportTemplateField CreateFieldEntity(
        Id<ReportTemplate> templateId,
        ReportTemplateFieldPersistenceModel field)
        => new()
        {
            Id = field.Id,
            TemplateId = templateId,
            Key = field.Key,
            Label = field.Label,
            Type = field.Type,
            IsRequired = field.IsRequired,
            Order = field.Order,
            ModuleConfig = field.ModuleConfig,
            CreatedAt = field.CreatedAt
        };
}
