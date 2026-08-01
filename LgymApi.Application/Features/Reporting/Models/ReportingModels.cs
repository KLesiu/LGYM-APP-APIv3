using System.Text.Json;
using LgymApi.Domain.Enums;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Features.Reporting.Models;

public sealed class CreateReportTemplateCommand
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<ReportTemplateFieldCommand> Fields { get; set; } = [];
}

public sealed class ReportTemplateFieldCommand
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ReportFieldType Type { get; set; }
    public bool IsRequired { get; set; }
    public int Order { get; set; }
    public JsonElement? ModuleConfig { get; set; }
}

public sealed class CreateReportRequestCommand
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportTemplate> TemplateId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string? Note { get; set; }
}

public sealed class SubmitReportRequestCommand
{
    public Dictionary<string, JsonElement> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UpdateReportSubmissionFeedbackCommand
{
    public string? TrainerOverallComment { get; set; }
    public Dictionary<string, string?> FieldComments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReportTemplateResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportTemplate> Id { get; set; }
    public LgymApi.Domain.ValueObjects.Id<AccountReference> TrainerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<ReportTemplateFieldResult> Fields { get; set; } = [];
}

public sealed class ReportTemplateFieldResult
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ReportFieldType Type { get; set; }
    public bool IsRequired { get; set; }
    public int Order { get; set; }
    public JsonElement? ModuleConfig { get; set; }
}

public sealed class ReportRequestResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportRequest> Id { get; set; }
    public LgymApi.Domain.ValueObjects.Id<AccountReference> TrainerId { get; set; }
    public LgymApi.Domain.ValueObjects.Id<AccountReference> TraineeId { get; set; }
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportTemplate> TemplateId { get; set; }
    public ReportRequestStatus Status { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public ReportTemplateResult Template { get; set; } = new();
}

public sealed class ReportSubmissionResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportSubmission> Id { get; set; }
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportRequest> ReportRequestId { get; set; }
    public LgymApi.Domain.ValueObjects.Id<AccountReference> TraineeId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public Dictionary<string, JsonElement> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? TrainerOverallComment { get; set; }
    public Dictionary<string, string> TrainerFieldComments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? TrainerFeedbackAddedAt { get; set; }
    public DateTimeOffset? TrainerFeedbackReadAt { get; set; }
    public ReportRequestResult Request { get; set; } = new();
}

public sealed class InitiatePhotoUploadCommand
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportRequest> ReportRequestId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}

public sealed class InitiatePhotoUploadResult
{
    public string UploadUrl { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class SignedReadUrlResult
{
    public string ReadUrl { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CompletePhotoUploadCommand
{
    public string StorageKey { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportRequest> ReportRequestId { get; set; }
    public string ViewType { get; set; } = string.Empty;
}

public sealed class CompletePhotoUploadResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Photo> PhotoId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class GetPhotoHistoryCommand
{
    public LgymApi.Domain.ValueObjects.Id<AccountReference>? TraineeId { get; set; }
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportRequest>? RequestId { get; set; }
}

public sealed class PhotoHistoryItemResult
{
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.Photo> Id { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string ReadUrl { get; set; } = string.Empty;
    public LgymApi.Domain.ValueObjects.Id<LgymApi.Domain.Entities.ReportRequest> ReportRequestId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}
