using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        bool isDevelopment,
        bool isTesting)
    {
        var photoStorageOptions = BuildPhotoStorageOptions(configuration);

        services.AddSingleton(photoStorageOptions);
        services.AddSingleton<LocalPhotoDevelopmentStore>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        RegisterPhotoStorageProvider(services, photoStorageOptions, isDevelopment, isTesting);
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
        bool isDevelopment,
        bool isTesting)
    {
        if (string.Equals(options.Provider, "CloudflareR2", StringComparison.OrdinalIgnoreCase))
        {
            ValidateCloudflareR2Options(options);
            RegisterLocalPhotoDevelopmentUrlSigner(services, signingKey: null);
            services.AddScoped<IPhotoStorageProvider, CloudflareR2PhotoStorageProvider>();
            return;
        }

        if (string.Equals(options.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            if (!isDevelopment && !isTesting)
            {
                throw new InvalidOperationException("LocalPhotoStorageProvider cannot be used outside Development.");
            }

            var signingKey = ResolveLocalDevelopmentSigningKey(options, isDevelopment, isTesting);
            RegisterLocalPhotoDevelopmentUrlSigner(services, signingKey);
            services.AddScoped<IPhotoStorageProvider, LocalPhotoStorageProvider>();
            return;
        }

        throw new InvalidOperationException($"Unsupported photo storage provider: {options.Provider}");
    }

    private static string ResolveLocalDevelopmentSigningKey(
        PhotoStorageOptions options,
        bool isDevelopment,
        bool isTesting)
    {
        if (isTesting && string.IsNullOrEmpty(options.LocalDevelopmentSigningKey))
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        if (!isDevelopment && !isTesting)
        {
            throw new InvalidOperationException("LocalPhotoStorageProvider cannot be used outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.LocalDevelopmentSigningKey) ||
            Encoding.UTF8.GetByteCount(options.LocalDevelopmentSigningKey) < 32)
        {
            throw new InvalidOperationException(
                "PhotoStorage:LocalDevelopmentSigningKey must contain at least 32 UTF-8 bytes when the Local provider is enabled in Development.");
        }

        return options.LocalDevelopmentSigningKey;
    }

    private static void RegisterLocalPhotoDevelopmentUrlSigner(
        IServiceCollection services,
        string? signingKey)
    {
        services.AddSingleton<LocalPhotoDevelopmentUrlSigner>(serviceProvider => new LocalPhotoDevelopmentUrlSigner(
            signingKey,
            serviceProvider.GetRequiredService<TimeProvider>()));
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
