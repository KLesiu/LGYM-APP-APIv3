using System.Text.Json;
using LgymApi.Application.Features.Measurements;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Identity.Contracts;

namespace LgymApi.Application.Features.Reporting;

public interface IReportSubmissionAcceptedProgressCommandFactory
{
    Dictionary<string, JsonElement> FilterInvalidMeasurementAnswers(
        ReportTemplatePersistenceModel template,
        IReadOnlyDictionary<string, JsonElement> answers);

    ReportSubmissionAcceptedProgressCommand? Create(
        ReportTemplatePersistenceModel template,
        IReadOnlyDictionary<string, JsonElement> answers,
        Id<LgymApi.Domain.Entities.ReportSubmission> submissionId,
        Id<LgymApi.Domain.Entities.ReportRequest> requestId,
        Id<AccountReference> traineeId,
        DateTimeOffset acceptedAtUtc);
}

public sealed class ReportSubmissionAcceptedProgressCommandFactory : IReportSubmissionAcceptedProgressCommandFactory
{
    public Dictionary<string, JsonElement> FilterInvalidMeasurementAnswers(
        ReportTemplatePersistenceModel template,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        var filteredAnswers = new Dictionary<string, JsonElement>(answers, StringComparer.OrdinalIgnoreCase);

        foreach (var field in template.Fields.Where(field => field.Type == ReportFieldType.Measurements))
        {
            if (string.IsNullOrWhiteSpace(field.Key)
                || string.IsNullOrWhiteSpace(field.ModuleConfig)
                || !answers.TryGetValue(field.Key, out var fieldAnswer)
                || fieldAnswer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            using var moduleConfigDocument = JsonDocument.Parse(field.ModuleConfig);
            if (!ReportingModuleConfigParser.TryNormalizeMeasurementModuleConfig(
                    moduleConfigDocument.RootElement,
                    out _,
                    out var allowedBodyParts))
            {
                continue;
            }

            var allowedBodyPartSet = allowedBodyParts.ToHashSet();
            var validMeasurements = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var measurementProperty in fieldAnswer.EnumerateObject())
            {
                if (ReportingModuleConfigParser.TryResolveBodyPart(measurementProperty.Name, out var bodyPart)
                    && allowedBodyPartSet.Contains(bodyPart)
                    && TryParseMeasurement(measurementProperty.Value, bodyPart, out _))
                {
                    validMeasurements[measurementProperty.Name] = measurementProperty.Value;
                }
            }

            filteredAnswers[field.Key] = JsonSerializer.SerializeToElement(validMeasurements);
        }

        return filteredAnswers;
    }

    public ReportSubmissionAcceptedProgressCommand? Create(
        ReportTemplatePersistenceModel template,
        IReadOnlyDictionary<string, JsonElement> answers,
        Id<LgymApi.Domain.Entities.ReportSubmission> submissionId,
        Id<LgymApi.Domain.Entities.ReportRequest> requestId,
        Id<AccountReference> traineeId,
        DateTimeOffset acceptedAtUtc)
    {
        var measurements = CollectMeasurements(template, answers);
        if (measurements.Count == 0)
        {
            return null;
        }

        var acceptedAt = acceptedAtUtc.ToUniversalTime();
        var submissionIdValue = submissionId.ToString();

        return new ReportSubmissionAcceptedProgressCommand
        {
            Event = new ReportSubmissionAcceptedProgressPayload(
                ReportSubmissionAcceptedProgressPayload.CurrentSchemaVersion,
                submissionIdValue,
                submissionIdValue,
                requestId.ToString(),
                submissionIdValue,
                traineeId,
                acceptedAt,
                acceptedAt,
                measurements)
        };
    }

    private List<ReportSubmissionAcceptedProgressMeasurement> CollectMeasurements(
        ReportTemplatePersistenceModel template,
        IReadOnlyDictionary<string, JsonElement> answers)
    {
        var candidates = new Dictionary<BodyParts, ReportSubmissionAcceptedProgressMeasurement>();
        var filteredAnswers = FilterInvalidMeasurementAnswers(template, answers);

        foreach (var field in template.Fields.Where(field => field.Type == ReportFieldType.Measurements))
        {
            if (string.IsNullOrWhiteSpace(field.Key)
                || string.IsNullOrWhiteSpace(field.ModuleConfig)
                || !filteredAnswers.TryGetValue(field.Key, out var fieldAnswer)
                || fieldAnswer.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var measurementProperty in fieldAnswer.EnumerateObject())
            {
                if (!ReportingModuleConfigParser.TryResolveBodyPart(measurementProperty.Name, out var bodyPart)
                    || candidates.ContainsKey(bodyPart)
                    || !TryParseMeasurement(measurementProperty.Value, bodyPart, out var measurement))
                {
                    continue;
                }

                candidates.Add(bodyPart, measurement);
            }
        }

        return candidates.Values.ToList();
    }

    private static bool TryParseMeasurement(
        JsonElement rawValue,
        BodyParts bodyPart,
        out ReportSubmissionAcceptedProgressMeasurement measurement)
    {
        measurement = default!;

        if (rawValue.ValueKind != JsonValueKind.Object
            || !rawValue.TryGetProperty("value", out var valueElement)
            || !valueElement.TryGetDouble(out var value)
            || !double.IsFinite(value)
            || value <= 0
            || !rawValue.TryGetProperty("unit", out var unitElement)
            || unitElement.ValueKind != JsonValueKind.String
            || !MeasurementUnitResolver.TryParseStoredUnit(unitElement.GetString(), out var unit)
            || !MeasurementUnitResolver.IsUnitAllowedForBodyPart(bodyPart, unit))
        {
            return false;
        }

        measurement = new ReportSubmissionAcceptedProgressMeasurement(bodyPart, value, unit);
        return true;
    }
}
