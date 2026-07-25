using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class ReportRequestSeeder : IEntitySeeder
{
    public int Order => 82;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("report requests");
        if (seedContext.ReportRequests.Count > 0)
        {
            SeedOperationConsole.Skip("report requests");
            return;
        }

        var trainer = seedContext.DemoUsers.FirstOrDefault();
        var trainee = seedContext.DemoUsers.Skip(1).FirstOrDefault();
        var template = seedContext.ReportTemplates.FirstOrDefault();

        if (trainer == null || trainee == null || template == null)
        {
            SeedOperationConsole.Skip("report requests");
            return;
        }

        var existing = await context.ReportRequests
            .AsNoTracking()
            .Select(request => new { request.TrainerId, request.TraineeId, request.TemplateId })
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<(Id<User> TrainerId, Id<User> TraineeId, Id<ReportTemplate> TemplateId)>(
            existing.Select(entry => (entry.TrainerId, entry.TraineeId, entry.TemplateId)));

        var request = new ReportRequest
        {
            Id = Id<ReportRequest>.New(),
            TrainerId = trainer.Id,
            TraineeId = trainee.Id,
            TemplateId = template.Id,
            Status = ReportRequestStatus.Pending,
            DueAt = DateTimeOffset.UtcNow.AddDays(7),
            Note = "Please fill in your weekly check-in."
        };

        if (!existingSet.Add((request.TrainerId, request.TraineeId, request.TemplateId)))
        {
            SeedOperationConsole.Skip("report requests");
            return;
        }

        await context.ReportRequests.AddAsync(request, cancellationToken);
        seedContext.ReportRequests.Add(request);

        if (seedContext.ReportSubmissions.Any())
        {
            SeedOperationConsole.Done("report requests");
            return;
        }

        var submissionExists = await context.ReportSubmissions
            .AsNoTracking()
            .AnyAsync(submission => submission.ReportRequestId == request.Id, cancellationToken);
        if (!submissionExists)
        {
            var submission = new ReportSubmission
            {
                Id = Id<ReportSubmission>.New(),
                ReportRequestId = request.Id,
                TraineeId = request.TraineeId,
                PayloadJson = "{\"weight\":80,\"sleep\":7,\"notes\":\"All good\"}"
            };

            await context.ReportSubmissions.AddAsync(submission, cancellationToken);
            seedContext.ReportSubmissions.Add(submission);
        }

        SeedOperationConsole.Done("report requests");
    }
}
