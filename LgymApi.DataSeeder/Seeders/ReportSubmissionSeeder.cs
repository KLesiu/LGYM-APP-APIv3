using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.DataSeeder.Seeders;

public sealed class ReportSubmissionSeeder : IEntitySeeder
{
    public int Order => 83;

    public async Task SeedAsync(AppDbContext context, SeedContext seedContext, CancellationToken cancellationToken)
    {
        SeedOperationConsole.Start("report submissions");
        if (seedContext.ReportSubmissions.Count > 0)
        {
            SeedOperationConsole.Skip("report submissions");
            return;
        }

        var requestIdSet = seedContext.ReportRequests.Select(request => request.Id).ToHashSet();
        if (requestIdSet.Count == 0)
        {
            SeedOperationConsole.Skip("report submissions");
            return;
        }

        var existing = await context.ReportSubmissions
            .AsNoTracking()
            .Select(submission => submission.ReportRequestId)
            .ToListAsync(cancellationToken);

        var existingRequestIdSet = new HashSet<Id<ReportRequest>>(existing);

        var addedAny = false;
        foreach (var requestId in requestIdSet)
        {
            if (!existingRequestIdSet.Add(requestId))
            {
                continue;
            }

            var request = seedContext.ReportRequests.First(entry => entry.Id == requestId);
            var submission = new ReportSubmission
            {
                Id = Id<ReportSubmission>.New(),
                ReportRequestId = request.Id,
                TraineeId = request.TraineeId,
                PayloadJson = "{\"weight\":80,\"sleep\":7,\"notes\":\"All good\"}"
            };

            await context.ReportSubmissions.AddAsync(submission, cancellationToken);
            seedContext.ReportSubmissions.Add(submission);
            addedAny = true;
        }

        if (!addedAny)
        {
            SeedOperationConsole.Skip("report submissions");
            return;
        }

        SeedOperationConsole.Done("report submissions");
    }
}
