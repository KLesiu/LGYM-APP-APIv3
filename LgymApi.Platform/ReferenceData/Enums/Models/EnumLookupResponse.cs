namespace LgymApi.Application.Platform.ReferenceData.Enums.Models;

public sealed class EnumLookupResponse
{
    public string EnumType { get; init; } = string.Empty;
    public List<EnumLookupEntry> Values { get; init; } = new();
}
