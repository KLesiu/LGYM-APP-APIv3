using System.Collections;
using LgymApi.Application.Pagination;

namespace LgymApi.Infrastructure.Pagination;

internal sealed partial class FilterToGridifyAdapter
{
    private void ValidatePage(int page)
    {
        if (page < 1 || page > _maxPage)
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, $"Page must be between 1 and {_maxPage}.");
        }
    }

    private void ValidatePageSize(int pageSize)
    {
        if (pageSize < 1 || pageSize > _maxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, $"Page size must be between 1 and {_maxPageSize}.");
        }
    }

    private void ValidateSortDescriptors(IEnumerable<SortDescriptor> sortDescriptors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sortDescriptor in sortDescriptors)
        {
            if (string.IsNullOrWhiteSpace(sortDescriptor.FieldName) || !FieldNamePattern().IsMatch(sortDescriptor.FieldName))
            {
                throw new ArgumentException($"Sort field '{sortDescriptor.FieldName}' is not valid.", nameof(sortDescriptors));
            }

            if (!_fields.TryGetValue(sortDescriptor.FieldName, out var field) || !field.AllowSort)
            {
                throw new ArgumentException($"Sort field '{sortDescriptor.FieldName}' is not allowed.", nameof(sortDescriptors));
            }

            if (!seen.Add(sortDescriptor.FieldName))
            {
                throw new ArgumentException($"Duplicate sort field '{sortDescriptor.FieldName}' is not allowed.", nameof(sortDescriptors));
            }
        }
    }

    private string BuildGroup(FilterGroup group, int depth)
    {
        if (depth > _maxNestingDepth)
        {
            throw new ArgumentException($"Filter nesting depth cannot exceed {_maxNestingDepth}.", nameof(group));
        }

        if (!Enum.IsDefined(group.Operator))
        {
            throw new ArgumentException($"Group operator '{group.Operator}' is invalid.", nameof(group));
        }

        var fragments = new List<string>(group.Conditions.Count + group.Groups.Count);

        fragments.AddRange(group.Conditions.Select(BuildCondition));
        fragments.AddRange(group.Groups.Select(child => BuildGroup(child, depth + 1)));

        if (fragments.Count == 0)
        {
            throw new ArgumentException("Filter groups must contain at least one condition or child group.", nameof(group));
        }

        if (fragments.Count == 1)
        {
            return fragments[0];
        }

        var separator = group.Operator == GroupOperator.And ? "&" : "|";
        return $"({string.Join(separator, fragments)})";
    }

    private string BuildCondition(FilterCondition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.FieldName) || !FieldNamePattern().IsMatch(condition.FieldName))
        {
            throw new ArgumentException($"Filter field '{condition.FieldName}' is not valid.", nameof(condition));
        }

        if (!_fields.TryGetValue(condition.FieldName, out var field) || !field.AllowFilter)
        {
            throw new ArgumentException($"Filter field '{condition.FieldName}' is not allowed.", nameof(condition));
        }

        if (!Enum.IsDefined(condition.Operator))
        {
            throw new ArgumentException($"Filter operator '{condition.Operator}' is not valid.", nameof(condition));
        }

        ValidateOperator(field, condition.Operator);

        if (condition.Operator == FilterOperator.In)
        {
            return BuildInCondition(condition, field);
        }

        var normalizedValue = NormalizeSingleValue(condition.Value, field.FieldType, condition.FieldName);
        return $"{condition.FieldName}{GetOperatorToken(condition.Operator)}{normalizedValue}";
    }

    private string BuildInCondition(FilterCondition condition, GridifyFieldDefinition field)
    {
        var unwrapped = UnwrapJsonElement(condition.Value);

        if (unwrapped is string || unwrapped is not IEnumerable values)
        {
            throw new ArgumentException($"IN operator for field '{condition.FieldName}' requires a non-empty collection.", nameof(condition));
        }

        var fragments = new List<string>();

        foreach (var value in values)
        {
            fragments.Add($"{condition.FieldName}={NormalizeSingleValue(value, field.FieldType, condition.FieldName)}");
        }

        if (fragments.Count == 0)
        {
            throw new ArgumentException($"IN operator for field '{condition.FieldName}' requires at least one value.", nameof(condition));
        }

        return fragments.Count == 1 ? fragments[0] : $"({string.Join("|", fragments)})";
    }

    private static void ValidateOperator(GridifyFieldDefinition field, FilterOperator filterOperator)
    {
        var normalizedType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        HashSet<FilterOperator> allowedOperators = normalizedType switch
        {
            _ when normalizedType == typeof(string) => StringOperators,
            _ when IsComparableType(normalizedType) => ComparableOperators,
            _ => EqualityOperators
        };

        if (!allowedOperators.Contains(filterOperator))
        {
            throw new ArgumentException(
                $"Operator '{filterOperator}' is not allowed for field '{field.FieldName}' of type '{normalizedType.Name}'.",
                nameof(filterOperator));
        }
    }

    private static bool IsComparableType(Type type)
        => type.IsEnum
           || IsNumericType(type)
           || type == typeof(DateTime)
           || type == typeof(DateTimeOffset)
           || type == typeof(DateOnly)
           || type == typeof(TimeOnly);

    private static bool IsNumericType(Type type)
        => Type.GetTypeCode(type) is TypeCode.Byte
            or TypeCode.SByte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Decimal;
}
