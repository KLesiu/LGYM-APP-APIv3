using System.Globalization;
using LgymApi.Application.Abstractions.Storage;
using LgymApi.Application.Features.Reporting;
using LgymApi.Application.Options;
using LgymApi.Application.Reporting.Persistence;
using LgymApi.Application.WorkoutProgress.ReportingIntegration;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Repositories;
using LgymApi.Infrastructure.Repositories.Reporting;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopmentOrTesting)
    {
        var photoStorageOptions = BuildPhotoStorageOptions(configuration);

        services.AddSingleton(photoStorageOptions);
        services.AddSingleton<LocalPhotoDevelopmentStore>();
        RegisterPhotoStorageProvider(services, photoStorageOptions, isDevelopmentOrTesting);
        services.AddScoped<IReportTemplatePersistence, ReportTemplatePersistenceRepository>();
        services.AddScoped<IReportRequestSubmissionPersistence, ReportRequestSubmissionPersistenceRepository>();
        services.AddScoped<IRecurringReportAssignmentPersistence, RecurringReportAssignmentPersistenceRepository>();
        services.AddScoped<IReportPhotoPersistence, ReportPhotoPersistenceRepository>();
        services.AddScoped<IReportingRelationshipAccessPersistence, ReportingRelationshipAccessPersistenceRepository>();
        services.AddScoped<IReportSubmissionAcceptedProgressPersistence, ReportSubmissionAcceptedProgressPersistenceRepository>();

        return services;
    }

    private static void RegisterPhotoStorageProvider(
        IServiceCollection services,
        PhotoStorageOptions options,
        bool isDevelopmentOrTesting)
    {
        if (string.Equals(options.Provider, "CloudflareR2", StringComparison.OrdinalIgnoreCase))
        {
            ValidateCloudflareR2Options(options);
            services.AddScoped<IPhotoStorageProvider, CloudflareR2PhotoStorageProvider>();
            return;
        }

        if (string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            if (!isDevelopmentOrTesting)
            {
                throw new InvalidOperationException("LocalPhotoStorageProvider cannot be used outside Development.");
            }

            services.AddScoped<IPhotoStorageProvider, LocalPhotoStorageProvider>();
            return;
        }

        throw new InvalidOperationException($"Unsupported photo storage provider: {options.Provider}");
    }

    private static PhotoStorageOptions BuildPhotoStorageOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection("PhotoStorage").Get<PhotoStorageOptions>() ?? new PhotoStorageOptions();

        options.Provider = string.IsNullOrWhiteSpace(options.Provider) ? "Local" : options.Provider.Trim();
        options.AllowedMimeTypes = options.AllowedMimeTypes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (options.AllowedMimeTypes.Count == 0)
        {
            options.AllowedMimeTypes = ["image/jpeg", "image/png", "image/heic"];
        }

        return options;
    }

    private static void ValidateCloudflareR2Options(PhotoStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new InvalidOperationException("PhotoStorage:BucketName is required for CloudflareR2.");
        }

        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("PhotoStorage:Endpoint is required for CloudflareR2.");
        }

        if (string.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            throw new InvalidOperationException("PhotoStorage:AccessKeyId is required for CloudflareR2.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            throw new InvalidOperationException("PhotoStorage:SecretAccessKey is required for CloudflareR2.");
        }
    }
}
