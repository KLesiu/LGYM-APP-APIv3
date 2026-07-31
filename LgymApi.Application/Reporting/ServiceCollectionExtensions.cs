using LgymApi.Application.Features.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Application.Reporting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.AddSingleton<IReportSubmissionAcceptedProgressCommandFactory, ReportSubmissionAcceptedProgressCommandFactory>();
        services.AddScoped<IReportingService, ReportingService>();
        services.AddScoped<IRecurringReportAssignmentService, RecurringReportAssignmentService>();
        services.AddScoped<IExpiredPhotoUploadCleanupService, ExpiredPhotoUploadCleanupService>();

        return services;
    }
}
