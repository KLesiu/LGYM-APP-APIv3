using System.Globalization;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Platform.ReferenceData.Enums;

internal static class EnumLookupContextKeys
{
    public static readonly ContextKey<CultureInfo> Culture = new("ReferenceData.Enums.Culture");
}
