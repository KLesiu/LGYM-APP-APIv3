using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class ReportTemplateSeeder : IEntitySeeder
{
    public int Order => 80;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("report templates");
        if (seedContext.ReportTemplates.Count > 0)
        {
            SeedOperationConsole.Skip("report templates");
            return;
        }

        var trainer = seedContext.DemoUsers.FirstOrDefault();
        if (trainer == null)
        {
            SeedOperationConsole.Skip("report templates");
            return;
        }

        var existing = await context.ReportTemplates
            .AsNoTracking()
            .Select(template => new { template.TrainerId, template.Name })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> TrainerId, string Name)>(
            existing.Select(entry => (entry.TrainerId, entry.Name)));

        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = trainer.Id,
            Name = "Weekly Check-in",
            Description = "Default weekly progress report"
        };

        var templateAdded = false;
        if (existingSet.Add((template.TrainerId, template.Name)))
        {
            await context.ReportTemplates.AddAsync(template, cancellationToken);
            seedContext.ReportTemplates.Add(template);
            templateAdded = true;
        }

        var fieldTemplateId = templateAdded
            ? template.Id
            : seedContext.ReportTemplates.FirstOrDefault(t => t.TrainerId == trainer.Id && t.Name == template.Name)?.Id;

        if (fieldTemplateId == null)
        {
            SeedOperationConsole.Skip("report templates");
            return;
        }

        var fieldExisting = await context.ReportTemplateFields
            .AsNoTracking()
            .Where(field => field.TemplateId == fieldTemplateId.Value)
            .Select(field => field.Key)
           .ToListAsync(cancellationToken);

        var fields = new List<ReportTemplateField>
          {
              new()
              {
                  Id = Id<ReportTemplateField>.New(),
                  TemplateId = fieldTemplateId.Value,
                  Key = "weight",
                Label = "Current weight",
                Type = ReportFieldType.Number,
                IsRequired = true,
                Order = 1
            },
              new()
              {
                  Id = Id<ReportTemplateField>.New(),
                  TemplateId = fieldTemplateId.Value,
                  Key = "sleep",
                Label = "Average sleep (hours)",
                Type = ReportFieldType.Number,
                IsRequired = false,
                Order = 2
            },
              new()
              {
                  Id = Id<ReportTemplateField>.New(),
                  TemplateId = fieldTemplateId.Value,
                  Key = "notes",
                Label = "Notes",
                Type = ReportFieldType.Text,
                IsRequired = false,
                Order = 3
            }
        };

        var addedAny = false;
        foreach (var field in fields)
        {
            if (fieldExisting.Contains(field.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            await context.ReportTemplateFields.AddAsync(field, cancellationToken);
            seedContext.ReportTemplateFields.Add(field);
            addedAny = true;
        }

        if (!templateAdded && !addedAny)
        {
            SeedOperationConsole.Skip("report templates");
            return;
        }

        SeedOperationConsole.Done("report templates");
    }
}
