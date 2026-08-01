using System.Text.Json;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Application.Mapping.Core;

namespace LgymApi.Application.Reporting.Persistence;

public sealed class ReportingPersistenceMappingProfile : IMappingProfile
{
    public void Configure(MappingConfiguration configuration)
    {
        configuration.CreateMap<ReportTemplateFieldPersistenceModel, ReportTemplateFieldResult>((source, _) => new ReportTemplateFieldResult
        {
            Key = source.Key,
            Label = source.Label,
            Type = source.Type,
            IsRequired = source.IsRequired,
            Order = source.Order,
            ModuleConfig = string.IsNullOrWhiteSpace(source.ModuleConfig)
                ? null
                : JsonSerializer.Deserialize<JsonElement>(source.ModuleConfig)
        });

        configuration.CreateMap<ReportTemplatePersistenceModel, ReportTemplateResult>((source, context) => new ReportTemplateResult
        {
            Id = source.Id,
            TrainerId = source.TrainerId,
            Name = source.Name,
            Description = source.Description,
            CreatedAt = source.CreatedAt,
            Fields = context.MapList<ReportTemplateFieldPersistenceModel, ReportTemplateFieldResult>(source.Fields)
        });

        configuration.CreateMap<ReportRequestPersistenceModel, ReportRequestResult>((source, context) => new ReportRequestResult
        {
            Id = source.Id,
            TrainerId = source.TrainerId,
            TraineeId = source.TraineeId,
            TemplateId = source.TemplateId,
            Status = source.Status,
            DueAt = source.DueAt,
            Note = source.Note,
            CreatedAt = source.CreatedAt,
            SubmittedAt = source.SubmittedAt,
            Template = context.Map<ReportTemplatePersistenceModel, ReportTemplateResult>(source.Template)
        });

        configuration.CreateMap<ReportSubmissionPersistenceModel, ReportSubmissionResult>((source, context) => new ReportSubmissionResult
        {
            Id = source.Id,
            ReportRequestId = source.ReportRequestId,
            TraineeId = source.TraineeId,
            SubmittedAt = source.CreatedAt,
            Answers = DeserializeJsonDictionary<JsonElement>(source.PayloadJson),
            TrainerOverallComment = source.TrainerOverallComment,
            TrainerFieldComments = DeserializeJsonDictionary<string>(source.TrainerFieldCommentsJson),
            TrainerFeedbackAddedAt = source.TrainerFeedbackAddedAt,
            TrainerFeedbackReadAt = source.TrainerFeedbackReadAt,
            Request = context.Map<ReportRequestPersistenceModel, ReportRequestResult>(source.ReportRequest)
        });

        configuration.CreateMap<RecurringReportAssignmentPersistenceModel, RecurringReportAssignmentResult>((source, context) => new RecurringReportAssignmentResult
        {
            Id = source.Id,
            TrainerId = source.TrainerId,
            TraineeId = source.TraineeId,
            TemplateId = source.TemplateId,
            IntervalValue = source.IntervalValue,
            IntervalUnit = source.IntervalUnit,
            StartsAt = source.StartsAt,
            EndsAt = source.EndsAt,
            IsActive = source.IsActive,
            Note = source.Note,
            CurrentReportRequestId = source.CurrentReportRequestId,
            LastRequestCreatedAt = source.LastRequestCreatedAt,
            NextEligibleAt = source.NextEligibleAt,
            CreatedAt = source.CreatedAt,
            Template = context.Map<ReportTemplatePersistenceModel, ReportTemplateResult>(source.Template),
            CurrentReportRequest = source.CurrentReportRequest is null
                ? null
                : context.Map<ReportRequestPersistenceModel, ReportRequestResult>(source.CurrentReportRequest)
        });
    }

    private static Dictionary<string, TValue> DeserializeJsonDictionary<TValue>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
        }

        var values = JsonSerializer.Deserialize<Dictionary<string, TValue>>(json);
        return values is null
            ? new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TValue>(values, StringComparer.OrdinalIgnoreCase);
    }
}
