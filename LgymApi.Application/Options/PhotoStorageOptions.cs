namespace LgymApi.Application.Options;

public sealed class PhotoStorageOptions
{
    public string Provider { get; set; } = "Local";
    public string BucketName { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string LocalDevelopmentSigningKey { get; set; } = string.Empty;
    public int SignedUploadExpirationMinutes { get; set; } = 10;
    public int SignedReadExpirationMinutes { get; set; } = 15;
    public long MaxFileSizeBytes { get; set; } = 5_242_880;
    public List<string> AllowedMimeTypes { get; set; } = ["image/jpeg", "image/png", "image/heic"];
    public long DevMaxTotalBytes { get; set; } = 8_589_934_592;
    public int DevMaxUploadsPerDay { get; set; } = 200;
    public int DevMaxUploadInitPerUserPerHour { get; set; } = 50;
}
