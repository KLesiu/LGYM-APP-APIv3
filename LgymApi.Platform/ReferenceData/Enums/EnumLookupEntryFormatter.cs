using System.Collections.Concurrent;
using System.Globalization;
using System.Resources;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;
using LgymApi.Domain.Enums;
using LgymApi.Resources;

namespace LgymApi.Application.Platform.ReferenceData.Enums;

internal sealed class EnumLookupEntryFormatter
{
    private static readonly ConcurrentDictionary<(Type EnumType, string EnumName), string> TranslationKeyCache = new();
    private static readonly ResourceManager EnumResourceManager =
        new("LgymApi.Resources.Resources.Enums", typeof(LgymApi.Resources.Enums).Assembly);

    internal EnumLookupEntry Format(System.Enum enumValue, MappingContext? context)
    {
        var enumName = enumValue.ToString();
        var enumType = enumValue.GetType();
        var translationKey = TranslationKeyCache.GetOrAdd(
            (enumType, enumName),
            key => $"{key.EnumType.Name}_{key.EnumName}");
        var culture = context?.Get(EnumLookupContextKeys.Culture) ?? CultureInfo.CurrentUICulture;
        var displayName = EnumResourceManager.GetString(translationKey, culture) ?? enumName;

        return new EnumLookupEntry
        {
            Id = enumName,
            Name = displayName,
            DisplayName = displayName
        };
    }
}
