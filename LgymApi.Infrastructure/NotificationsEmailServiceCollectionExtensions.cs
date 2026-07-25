using LgymApi.Application.Options;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure.Configuration;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Infrastructure;

public static partial class ServiceCollectionExtensions
{
    private static void AddEmailInfrastructure(
        IServiceCollection services,
        IConfiguration configuration,
        AppDefaultsOptions appDefaultsOptions)
    {
        var emailOptions = EmailOptionsFactory.Create(configuration, appDefaultsOptions);

        EmailOptionsFactory.Validate(emailOptions);

        services.AddSingleton(emailOptions);
        services.AddSingleton<IEmailNotificationsFeature, EmailNotificationsFeature>();
        services.AddSingleton<IEmailMetrics, EmailMetrics>();
        services.AddScoped<IEmailTemplateComposer, TrainerInvitationEmailTemplateComposer>();
        services.AddScoped<IEmailTemplateComposer, TrainerInvitationAcceptedEmailTemplateComposer>();
        services.AddScoped<IEmailTemplateComposer, TrainerInvitationRevokedEmailTemplateComposer>();
        services.AddScoped<IEmailTemplateComposer, TrainingCompletedEmailTemplateComposer>();
        services.AddScoped<IEmailTemplateComposer, WelcomeEmailTemplateComposer>();
        services.AddScoped<IEmailTemplateComposer, PasswordRecoveryEmailTemplateComposer>();
        services.AddScoped<IEmailTemplateComposerFactory, EmailTemplateComposerFactory>();
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<DummyEmailSender>();
        services.AddScoped<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<EmailOptions>();
            return options.DeliveryMode == EmailDeliveryMode.Dummy
                ? sp.GetRequiredService<DummyEmailSender>()
                : sp.GetRequiredService<SmtpEmailSender>();
        });
    }
}
