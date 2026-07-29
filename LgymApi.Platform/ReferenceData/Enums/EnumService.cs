using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Mapping.Core;
using LgymApi.Application.Platform.ReferenceData.Errors;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;
using LgymApi.Domain.Enums;
using LgymApi.Resources;

namespace LgymApi.Application.Platform.ReferenceData.Enums;

internal sealed class EnumService : IEnumService
{
    private static readonly ConcurrentDictionary<(Type EnumType, string EnumName), bool> HiddenValueCache = new();
    private readonly IMapper _mapper;

    private static readonly Dictionary<string, Type> EnumTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        { nameof(BodyParts), typeof(BodyParts) },
        { nameof(ExerciseEloFormula), typeof(ExerciseEloFormula) },
        { nameof(MeasurementUnits), typeof(MeasurementUnits) },
        { nameof(WeightUnits), typeof(WeightUnits) },
        { nameof(HeightUnits), typeof(HeightUnits) },
        { nameof(Platforms), typeof(Platforms) }
    };

    public EnumService(IMapper mapper)
    {
        _mapper = mapper;
    }

    public List<EnumLookupEntry> GetLookup<TEnum>(CultureInfo? culture = null) where TEnum : struct, System.Enum
    {
        return System.Enum.GetValues<TEnum>()
            .Cast<System.Enum>()
            .Where(e => !IsHidden(e))
            .Select(e => ToLookup(e, culture))
            .ToList();
    }

    public EnumLookupResponse? GetLookupByName(string enumTypeName, CultureInfo? culture = null)
    {
        if (!EnumTypes.TryGetValue(enumTypeName, out var enumType))
        {
            return null;
        }

        var values = new List<EnumLookupEntry>();
        foreach (System.Enum enumValue in System.Enum.GetValues(enumType))
        {
            if (IsHidden(enumValue))
            {
                continue;
            }

            values.Add(ToLookup(enumValue, culture));
        }

        return new EnumLookupResponse
        {
            EnumType = enumType.Name,
            Values = values
        };
    }

    public Task<Result<EnumLookupResponse, AppError>> GetLookupByNameAsync(string enumTypeName, CultureInfo? culture = null, CancellationToken ct = default)
    {
        var lookup = GetLookupByName(enumTypeName, culture);

        if (lookup == null)
        {
            return Task.FromResult(Result<EnumLookupResponse, AppError>.Failure(
                new InvalidEnumError(Messages.FieldRequired)));
        }

        return Task.FromResult(Result<EnumLookupResponse, AppError>.Success(lookup));
    }

    public List<string> GetAvailableEnumTypes()
    {
        return EnumTypes.Keys.OrderBy(k => k).ToList();
    }

    public EnumLookupEntry ToLookup(System.Enum enumValue, CultureInfo? culture = null)
    {
        var context = _mapper.CreateContext();
        if (culture is not null)
        {
            context.Set(EnumLookupContextKeys.Culture, culture);
        }

        return _mapper.Map<EnumLookupEntry>(enumValue, context);
    }

    private static bool IsHidden(System.Enum enumValue)
    {
        var enumType = enumValue.GetType();
        var enumName = enumValue.ToString();

        return HiddenValueCache.GetOrAdd((enumType, enumName), key =>
        {
            var field = key.EnumType.GetField(key.EnumName);
            return field?.GetCustomAttribute<HiddenAttribute>() != null;
        });
    }
}
