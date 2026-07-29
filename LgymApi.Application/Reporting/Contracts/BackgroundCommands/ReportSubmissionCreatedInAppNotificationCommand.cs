using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Entities;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Contracts.BackgroundCommands;

public sealed class ReportSubmissionCreatedInAppNotificationCommand : IActionCommand
{
    public Id<ReportSubmission> SubmissionId { get; init; }

    public Id<AccountReference> TrainerId { get; init; }

    public Id<AccountReference> TraineeId { get; init; }

    public string TemplateName { get; init; } = string.Empty;
}
