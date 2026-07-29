using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using LgymApi.Application.Features.Reporting;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ReportingServiceSubmissionHelpersTests
{
    [Test]
    public void ValidateAnswersAgainstTemplate_AllowsOptionalNullAndRejectsUnknownField()
    {
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = Id<User>.New(),
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField { Key = "requiredText", Type = ReportFieldType.Text, IsRequired = true },
                new ReportTemplateField { Key = "optionalNumber", Type = ReportFieldType.Number, IsRequired = false }
            ]
        };

        var validAnswers = new Dictionary<string, JsonElement>
        {
            ["requiredText"] = JsonDocument.Parse("\"ok\"").RootElement,
            ["optionalNumber"] = JsonDocument.Parse("null").RootElement
        };
        var invalidAnswers = new Dictionary<string, JsonElement>
        {
            ["requiredText"] = JsonDocument.Parse("\"ok\"").RootElement,
            ["extra"] = JsonDocument.Parse("1").RootElement
        };

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, validAnswers), "IsSuccess").Should().BeTrue();
        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, invalidAnswers), "IsFailure").Should().BeTrue();
    }

    [Test]
    public void ValidateAnswersAgainstTemplate_RejectsWrongValueKindsForDifferentFieldTypes()
    {
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = Id<User>.New(),
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField { Key = "date", Type = ReportFieldType.Date, IsRequired = true },
                new ReportTemplateField { Key = "flag", Type = ReportFieldType.Boolean, IsRequired = true },
                new ReportTemplateField { Key = "photos", Type = ReportFieldType.Photos, IsRequired = true },
                new ReportTemplateField { Key = "measurements", Type = ReportFieldType.Measurements, IsRequired = true }
            ]
        };

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, new Dictionary<string, JsonElement>
        {
            ["date"] = JsonDocument.Parse("\"not-a-date\"").RootElement,
            ["flag"] = JsonDocument.Parse("true").RootElement,
            ["photos"] = JsonDocument.Parse("[]").RootElement,
            ["measurements"] = JsonDocument.Parse("{}").RootElement
        }), "IsFailure").Should().BeTrue();

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, new Dictionary<string, JsonElement>
        {
            ["date"] = JsonDocument.Parse("\"2026-06-21T12:00:00Z\"").RootElement,
            ["flag"] = JsonDocument.Parse("1").RootElement,
            ["photos"] = JsonDocument.Parse("[]").RootElement,
            ["measurements"] = JsonDocument.Parse("{}").RootElement
        }), "IsFailure").Should().BeTrue();

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, new Dictionary<string, JsonElement>
        {
            ["date"] = JsonDocument.Parse("\"2026-06-21T12:00:00Z\"").RootElement,
            ["flag"] = JsonDocument.Parse("true").RootElement,
            ["photos"] = JsonDocument.Parse("\"bad\"").RootElement,
            ["measurements"] = JsonDocument.Parse("{}").RootElement
        }), "IsFailure").Should().BeTrue();

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, new Dictionary<string, JsonElement>
        {
            ["date"] = JsonDocument.Parse("\"2026-06-21T12:00:00Z\"").RootElement,
            ["flag"] = JsonDocument.Parse("true").RootElement,
            ["photos"] = JsonDocument.Parse("[]").RootElement,
            ["measurements"] = JsonDocument.Parse("[]").RootElement
        }), "IsFailure").Should().BeTrue();
    }

    [Test]
    public void ValidateAnswersAgainstTemplate_RejectsMeasurementEntriesOutsideConfiguredTypes()
    {
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = Id<User>.New(),
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField
                {
                    Key = "measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    ModuleConfig = """
                        { "measurementTypes": ["weight", "waist"] }
                        """
                }
            ]
        };

        var invalidAnswers = new Dictionary<string, JsonElement>
        {
            ["measurements"] = JsonDocument.Parse("""
                {
                    "chest": { "value": 100, "unit": "cm" }
                }
                """).RootElement
        };

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, invalidAnswers), "IsFailure").Should().BeTrue();
    }

    [Test]
    public void ValidateAnswersAgainstTemplate_AllowsConfiguredMeasurementEntries()
    {
        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = Id<User>.New(),
            Name = "Weekly",
            Fields =
            [
                new ReportTemplateField
                {
                    Key = "measurements",
                    Type = ReportFieldType.Measurements,
                    IsRequired = true,
                    ModuleConfig = """
                        { "measurementTypes": ["weight", "waist"] }
                        """
                }
            ]
        };

        var validAnswers = new Dictionary<string, JsonElement>
        {
            ["measurements"] = JsonDocument.Parse("""
                {
                    "weight": { "value": 81.5, "unit": "kg" },
                    "waist": { "value": 86, "unit": "cm" }
                }
                """).RootElement
        };

        GetBoolProperty(InvokeValidateAnswersAgainstTemplate(template, validAnswers), "IsSuccess").Should().BeTrue();
    }

    [Test]
    public void NormalizeTrainerFieldComments_TrimsValuesAndSkipsBlanks()
    {
        var method = typeof(ReportingService).GetMethod("NormalizeTrainerFieldComments", BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (Dictionary<string, string>)method.Invoke(null, [new Dictionary<string, string?>
        {
            ["Weight"] = "  keep going  ",
            [""] = "ignored",
            ["Empty"] = "   "
        }])!;

        result.Should().ContainSingle();
        result["Weight"].Should().Be("keep going");
    }

    [Test]
    public void ValidateTrainerFieldComments_RejectsUnknownKeys()
    {
        var template = new ReportTemplate
        {
            Fields = [new ReportTemplateField { Key = "weight" }]
        };
        var method = typeof(ReportingService).GetMethod("ValidateTrainerFieldComments", BindingFlags.Static | BindingFlags.NonPublic)!;

        var valid = InvokeResult(method, template, new Dictionary<string, string> { ["weight"] = "ok" });
        var invalid = InvokeResult(method, template, new Dictionary<string, string> { ["unknown"] = "bad" });

        GetBoolProperty(valid, "IsSuccess").Should().BeTrue();
        GetBoolProperty(invalid, "IsFailure").Should().BeTrue();
    }

    [Test]
    public void IsDuplicateSubmissionException_DetectsKnownDuplicateMarkers()
    {
        var method = typeof(ReportingService).GetMethod("IsDuplicateSubmissionException", BindingFlags.Static | BindingFlags.NonPublic)!;

        ((bool)method.Invoke(null, [new Exception("duplicate key in ReportSubmissions on ReportRequestId")])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new Exception("unique constraint")])!).Should().BeTrue();
        ((bool)method.Invoke(null, [new Exception("")])!).Should().BeFalse();
    }

    private static object InvokeValidateAnswersAgainstTemplate(ReportTemplate template, Dictionary<string, JsonElement> answers)
    {
        var method = typeof(ReportingService).GetMethod("ValidateAnswersAgainstTemplate", BindingFlags.Static | BindingFlags.NonPublic)!;
        return method.Invoke(null, [ReportingTestData.Template(template), answers])!;
    }

    private static object InvokeResult(MethodInfo method, ReportTemplate template, Dictionary<string, string> comments)
        => method.Invoke(null, [ReportingTestData.Template(template), comments])!;

    private static bool GetBoolProperty(object instance, string propertyName)
        => (bool)instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;
}
