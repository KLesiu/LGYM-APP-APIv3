using System.Text.Json;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Reporting.Errors;
using LgymApi.Application.BuildingBlocks.Results;
using LgymApi.Application.Features.Reporting.Models;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using UserEntity = LgymApi.Domain.Entities.User;

namespace LgymApi.Application.Features.Reporting;

public sealed partial class ReportingService : IReportingService
{
    public async Task<Result<ReportTemplateResult, AppError>> CreateTemplateAsync(UserEntity currentTrainer, CreateReportTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var trainerCheck = await EnsureTrainerAsync(currentTrainer, cancellationToken);
        if (trainerCheck.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(trainerCheck.Error);
        }

        var validationCheck = ValidateTemplateCommand(command);
        if (validationCheck.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(validationCheck.Error);
        }

        var template = new ReportTemplate
        {
            Id = Id<ReportTemplate>.New(),
            TrainerId = currentTrainer.Id,
            Name = command.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim(),
            IsDeleted = false,
            Fields = command.Fields
                .OrderBy(x => x.Order)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ReportTemplateField
                {
                    Id = Id<ReportTemplateField>.New(),
                    Key = x.Key.Trim(),
                    Label = x.Label.Trim(),
                    Type = x.Type,
                    IsRequired = x.IsRequired,
                    Order = x.Order,
                    ModuleConfig = NormalizeModuleConfig(x.Type, x.ModuleConfig)
                })
                .ToList()
        };

        await _reportingRepository.AddTemplateAsync(template, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<ReportTemplateResult, AppError>.Success(MapTemplate(template));
    }

    public async Task<Result<List<ReportTemplateResult>, AppError>> GetTrainerTemplatesAsync(UserEntity currentTrainer, CancellationToken cancellationToken = default)
    {
        var trainerCheck = await EnsureTrainerAsync(currentTrainer, cancellationToken);
        if (trainerCheck.IsFailure)
        {
            return Result<List<ReportTemplateResult>, AppError>.Failure(trainerCheck.Error);
        }

        var templates = await _reportingRepository.GetTemplatesByTrainerIdAsync(currentTrainer.Id, cancellationToken);
        return Result<List<ReportTemplateResult>, AppError>.Success(templates.Select(MapTemplate).ToList());
    }

    public async Task<Result<ReportTemplateResult, AppError>> GetTrainerTemplateAsync(UserEntity currentTrainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default)
    {
        var trainerCheck = await EnsureTrainerAsync(currentTrainer, cancellationToken);
        if (trainerCheck.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(trainerCheck.Error);
        }

        var templateResult = await EnsureOwnedTemplateAsync(currentTrainer, templateId, cancellationToken);
        if (templateResult.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(templateResult.Error);
        }

        return Result<ReportTemplateResult, AppError>.Success(MapTemplate(templateResult.Value));
    }

    public async Task<Result<ReportTemplateResult, AppError>> UpdateTemplateAsync(UserEntity currentTrainer, Id<ReportTemplate> templateId, CreateReportTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var trainerCheck = await EnsureTrainerAsync(currentTrainer, cancellationToken);
        if (trainerCheck.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(trainerCheck.Error);
        }

        var validationCheck = ValidateTemplateCommand(command);
        if (validationCheck.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(validationCheck.Error);
        }

        var templateResult = await EnsureOwnedTemplateAsync(currentTrainer, templateId, cancellationToken);
        if (templateResult.IsFailure)
        {
            return Result<ReportTemplateResult, AppError>.Failure(templateResult.Error);
        }

        var template = templateResult.Value;
        template.Name = command.Name.Trim();
        template.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();

        template.Fields.Clear();
        foreach (var field in command.Fields
                     .OrderBy(x => x.Order)
                     .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            template.Fields.Add(new ReportTemplateField
            {
                Id = Id<ReportTemplateField>.New(),
                TemplateId = template.Id,
                Key = field.Key.Trim(),
                Label = field.Label.Trim(),
                Type = field.Type,
                IsRequired = field.IsRequired,
                Order = field.Order,
                ModuleConfig = NormalizeModuleConfig(field.Type, field.ModuleConfig)
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<ReportTemplateResult, AppError>.Success(MapTemplate(template));
    }

    public async Task<Result<Unit, AppError>> DeleteTemplateAsync(UserEntity currentTrainer, Id<ReportTemplate> templateId, CancellationToken cancellationToken = default)
    {
        var trainerCheck = await EnsureTrainerAsync(currentTrainer, cancellationToken);
        if (trainerCheck.IsFailure)
        {
            return Result<Unit, AppError>.Failure(trainerCheck.Error);
        }

        var templateResult = await EnsureOwnedTemplateAsync(currentTrainer, templateId, cancellationToken);
        if (templateResult.IsFailure)
        {
            return Result<Unit, AppError>.Failure(templateResult.Error);
        }

        templateResult.Value.IsDeleted = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private static Result<Unit, AppError> ValidateTemplateCommand(CreateReportTemplateCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Fields.Count == 0)
        {
            return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        if (command.Fields.Any(field => string.IsNullOrWhiteSpace(field.Key) || string.IsNullOrWhiteSpace(field.Label)))
        {
            return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.FieldRequired));
        }

        var duplicateKey = command.Fields
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);

        if (duplicateKey != null)
        {
            return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
        }

        foreach (var field in command.Fields)
        {
            if (!System.Enum.IsDefined(field.Type))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }

            if (!IsValidModuleConfig(field.Type, field.ModuleConfig))
            {
                return Result<Unit, AppError>.Failure(new InvalidReportingError(Messages.ReportFieldValidationFailed));
            }
        }

        return Result<Unit, AppError>.Success(Unit.Value);
    }

    private static bool IsValidModuleConfig(ReportFieldType fieldType, JsonElement? moduleConfig)
    {
        return fieldType switch
        {
            ReportFieldType.Photos => ReportingModuleConfigParser.TryNormalizePhotoModuleConfig(moduleConfig, out _, out _),
            ReportFieldType.Measurements => ReportingModuleConfigParser.TryNormalizeMeasurementModuleConfig(moduleConfig, out _, out _),
            ReportFieldType.Text or ReportFieldType.Number or ReportFieldType.Boolean or ReportFieldType.Date
                => moduleConfig is null || !moduleConfig.Value.ValueKind.Equals(JsonValueKind.Object) && !moduleConfig.HasValue || !moduleConfig.HasValue,
            _ => false
        };
    }

    private static string? NormalizeModuleConfig(ReportFieldType fieldType, JsonElement? moduleConfig)
    {
        return fieldType switch
        {
            ReportFieldType.Photos when ReportingModuleConfigParser.TryNormalizePhotoModuleConfig(moduleConfig, out var normalizedPhotos, out _)
                => JsonSerializer.Serialize(normalizedPhotos),
            ReportFieldType.Measurements when ReportingModuleConfigParser.TryNormalizeMeasurementModuleConfig(moduleConfig, out var normalizedMeasurements, out _)
                => JsonSerializer.Serialize(normalizedMeasurements),
            _ => moduleConfig.HasValue ? JsonSerializer.Serialize(moduleConfig.Value) : null
        };
    }

    private static ReportTemplateResult MapTemplate(ReportTemplate template)
    {
        return new ReportTemplateResult
        {
            Id = template.Id,
            TrainerId = template.TrainerId,
            Name = template.Name,
            Description = template.Description,
            CreatedAt = template.CreatedAt,
            Fields = template.Fields
                .OrderBy(x => x.Order)
                .ThenBy(x => x.CreatedAt)
                .Select(x => new ReportTemplateFieldResult
                {
                    Key = x.Key,
                    Label = x.Label,
                    Type = x.Type,
                    IsRequired = x.IsRequired,
                    Order = x.Order,
                    ModuleConfig = string.IsNullOrWhiteSpace(x.ModuleConfig)
                        ? null
                        : JsonSerializer.Deserialize<JsonElement>(x.ModuleConfig)
                })
                .ToList()
        };
    }
}
