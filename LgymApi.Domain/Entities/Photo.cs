using LgymApi.Domain.ValueObjects;

namespace LgymApi.Domain.Entities;

public sealed class Photo : EntityBase<Photo>
{
    public string StorageKey { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public string? ThumbnailStorageKey { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public Id<ReportRequest> ReportRequestId { get; set; }
    public Id<User> UploaderUserId { get; set; }
    public Id<User> OwnerUserId { get; set; }

    public ReportRequest ReportRequest { get; set; } = null!;
    public User Uploader { get; set; } = null!;
    public User Owner { get; set; } = null!;
}
