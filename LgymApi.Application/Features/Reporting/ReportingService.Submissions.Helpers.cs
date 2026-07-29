using System.Globalization;
using System.Text.Json;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Enums;
using LgymApi.Resources;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService
{
    private static Result<Unit, AppError> ValidateAnswersAgainstTemplate(ReportTemplatePersistenceModel template, Dictionary<string, JsonElement> answers)
    {
        var expected = template.Fields.ToDictionary(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var field in template.Fields)
        {
            if (field.IsRequired && !answers.ContainsKey(field.Key))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }
        }

        foreach (var answer in answers)
        {
            if (!expected.TryGetValue(answer.Key, out var field))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }

            if (answer.Value.ValueKind == JsonValueKind.Null)
            {
                if (field.IsRequired)
                {
                    return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
                }

                continue;
            }

            if (!IsValueValidForType(answer.Value, field.Type))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }

            if (field.Type == ReportFieldType.Measurements
                && !AreMeasurementAnswersValid(field, answer.Value))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private static bool AreMeasurementAnswersValid(ReportTemplateFieldPersistenceModel field, JsonElement answer)
    {
        if (string.IsNullOrWhiteSpace(field.ModuleConfig)
            || answer.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        using var moduleConfigDocument = JsonDocument.Parse(field.ModuleConfig);
        if (!ReportingModuleConfigParser.TryNormalizeMeasurementModuleConfig(
                moduleConfigDocument.RootElement,
                out _,
                out var allowedBodyParts))
        {
            return false;
        }

        var allowedBodyPartSet = allowedBodyParts.ToHashSet();
        foreach (var measurementProperty in answer.EnumerateObject())
        {
            if (!ReportingModuleConfigParser.TryResolveBodyPart(measurementProperty.Name, out var bodyPart)
                || !allowedBodyPartSet.Contains(bodyPart)
                || !TryParseMeasurementEntry(measurementProperty.Value, bodyPart))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseMeasurementEntry(JsonElement rawValue, BodyParts bodyPart)
    {
        return rawValue.ValueKind == JsonValueKind.Object
               && rawValue.TryGetProperty("value", out var valueElement)
               && valueElement.TryGetDouble(out var value)
               && double.IsFinite(value)
               && value > 0
               && rawValue.TryGetProperty("unit", out var unitElement)
               && unitElement.ValueKind == JsonValueKind.String
               && MeasurementUnitResolver.TryParseStoredUnit(unitElement.GetString(), out var unit)
               && MeasurementUnitResolver.IsUnitAllowedForBodyPart(bodyPart, unit);
    }

    private static Dictionary<string, JsonElement> NormalizeAnswers(IReadOnlyDictionary<string, JsonElement> answers)
    {
        var normalizedAnswers = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var answer in answers)
        {
            normalizedAnswers[answer.Key] = answer.Value;
        }

        return normalizedAnswers;
    }

    private static Dictionary<string, string> NormalizeTrainerFieldComments(IReadOnlyDictionary<string, string?> comments)
    {
        var normalizedComments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var comment in comments)
        {
            if (string.IsNullOrWhiteSpace(comment.Key) || string.IsNullOrWhiteSpace(comment.Value))
            {
                continue;
            }

            normalizedComments[comment.Key] = comment.Value.Trim();
        }

        return normalizedComments;
    }

    private static Result<Unit, AppError> ValidateTrainerFieldComments(ReportTemplatePersistenceModel template, Dictionary<string, string> comments)
    {
        var expected = template.Fields
            .Select(field => field.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var commentKey in comments.Keys)
        {
            if (!expected.Contains(commentKey))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private static bool IsDuplicateSubmissionException(Exception exception)
    {
        var message = exception.ToString();
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("ReportRequestId", StringComparison.OrdinalIgnoreCase)
               || message.Contains("ReportSubmissions", StringComparison.OrdinalIgnoreCase)
               || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValueValidForType(JsonElement value, ReportFieldType type)
    {
        return type switch
        {
            ReportFieldType.Text => value.ValueKind == JsonValueKind.String,
            ReportFieldType.Number => value.ValueKind == JsonValueKind.Number,
            ReportFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ReportFieldType.Date => value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
            ReportFieldType.Photos => value.ValueKind is JsonValueKind.Array or JsonValueKind.Object,
            ReportFieldType.Measurements => value.ValueKind == JsonValueKind.Object,
            _ => false
        };
    }

    private static DateTimeOffset CalculateNextEligibleAt(DateTimeOffset readAt, int intervalValue, RecurringReportIntervalUnit intervalUnit)
    {
        return intervalUnit switch
        {
            RecurringReportIntervalUnit.Day => readAt.AddDays(intervalValue),
            RecurringReportIntervalUnit.Week => readAt.AddDays(intervalValue * 7d),
            RecurringReportIntervalUnit.Month => readAt.AddMonths(intervalValue),
            _ => readAt.AddDays(intervalValue)
        };
    }
}
