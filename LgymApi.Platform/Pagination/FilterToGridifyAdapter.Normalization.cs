using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

internal sealed partial class FilterToGridifyAdapter
{
    private static string NormalizeSingleValue(object? value, Type targetType, string fieldName)
    {
        value = UnwrapJsonElement(value);

        if (value is null)
        {
            throw new ArgumentException($"Field '{fieldName}' requires a value.", nameof(value));
        }

        var normalizedType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (normalizedType == typeof(string))
            {
                if (value is not string stringValue)
                {
                    throw new InvalidCastException();
                }

                return EscapeValue(stringValue);
            }

            if (IsGuidType(targetType))
            {
                return NormalizeGuidValue(value);
            }

            if (normalizedType == typeof(bool))
            {
                return value switch
                {
                    bool boolean => boolean.ToString().ToLowerInvariant(),
                    string booleanText when bool.TryParse(booleanText, out var parsed) => parsed.ToString().ToLowerInvariant(),
                    _ => throw new InvalidCastException()
                };
            }

            if (normalizedType == typeof(DateTime))
            {
                return value switch
                {
                    DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException()
                };
            }

            if (normalizedType == typeof(DateTimeOffset))
            {
                return value switch
                {
                    DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    string text when DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) => parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException()
                };
            }

            if (normalizedType == typeof(DateOnly))
            {
                return value switch
                {
                    DateOnly dateOnly => dateOnly.ToString("O", CultureInfo.InvariantCulture),
                    string text when DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed.ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException()
                };
            }

            if (normalizedType == typeof(TimeOnly))
            {
                return value switch
                {
                    TimeOnly timeOnly => timeOnly.ToString("O", CultureInfo.InvariantCulture),
                    string text when TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed.ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidCastException()
                };
            }

            if (normalizedType.IsEnum)
            {
                return value switch
                {
                    string text when Enum.TryParse(normalizedType, text, true, out var parsed) => parsed!.ToString()!,
                    _ when value.GetType() == normalizedType => value.ToString()!,
                    _ => throw new InvalidCastException()
                };
            }

            if (IsNumericType(normalizedType))
            {
                var converted = Convert.ChangeType(value, normalizedType, CultureInfo.InvariantCulture);
                return Convert.ToString(converted, CultureInfo.InvariantCulture) ?? throw new InvalidCastException();
            }

            return EscapeValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? throw new InvalidCastException());
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                $"Value '{value}' is not valid for field '{fieldName}' of type '{normalizedType.Name}'.",
                nameof(value),
                exception);
        }
    }

    private static object? UnwrapJsonElement(object? value)
    {
        if (value is not JsonElement jsonElement)
        {
            return value;
        }

        return jsonElement.ValueKind switch
        {
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement.TryGetInt64(out var longValue) ? longValue
                : jsonElement.TryGetDecimal(out var decimalValue) ? decimalValue
                : (object)jsonElement.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(element => UnwrapJsonElement(element)).ToList(),
            _ => jsonElement.GetRawText()
        };
    }


    private static string GetOperatorToken(FilterOperator filterOperator)
        => filterOperator switch
        {
            FilterOperator.Equals => "=",
            FilterOperator.NotEquals => "!=",
            FilterOperator.Contains => "=*",
            FilterOperator.StartsWith => "^",
            FilterOperator.EndsWith => "$",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterThanOrEqual => ">=",
            FilterOperator.LessThan => "<",
            FilterOperator.LessThanOrEqual => "<=",
            FilterOperator.In => "=",
            _ => throw new ArgumentOutOfRangeException(nameof(filterOperator), filterOperator, "Unsupported filter operator.")
        };

    private static string EscapeValue(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);

        for (var index = 0; index < value.Length; index++)
        {
            if (StartsWithCaseInsensitive(value, index, "/i"))
            {
                builder.Append("\\/i");
                index++;
                continue;
            }

            var character = value[index];

            if (character is '\\' or '(' or ')' or '|' or '&' or ',')
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool StartsWithCaseInsensitive(string source, int startIndex, string value)
        => startIndex + value.Length <= source.Length
           && string.Compare(source, startIndex, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldNamePattern();
}
