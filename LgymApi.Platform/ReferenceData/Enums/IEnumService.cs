using System.Globalization;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;

namespace LgymApi.Application.Platform.ReferenceData.Enums;

public interface IEnumService
{
    List<EnumLookupEntry> GetLookup<TEnum>(CultureInfo? culture = null) where TEnum : struct, System.Enum;
    EnumLookupResponse? GetLookupByName(string enumTypeName, CultureInfo? culture = null);
    Task<Result<EnumLookupResponse, AppError>> GetLookupByNameAsync(string enumTypeName, CultureInfo? culture = null, CancellationToken ct = default);
    List<string> GetAvailableEnumTypes();
    EnumLookupEntry ToLookup(System.Enum enumValue, CultureInfo? culture = null);
}
