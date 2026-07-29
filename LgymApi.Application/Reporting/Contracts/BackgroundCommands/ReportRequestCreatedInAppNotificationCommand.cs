using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.Domain.ValueObjects;
using LgymApi.Domain.Entities;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Reporting.Contracts.BackgroundCommands;

public sealed class ReportRequestCreatedInAppNotificationCommand : IActionCommand
{
    public Id<ReportRequest> RequestId { get; init; }

    public Id<AccountReference> TraineeId { get; init; }

    public Id<AccountReference> TrainerId { get; init; }

    public string TemplateName { get; init; } = string.Empty;
}
