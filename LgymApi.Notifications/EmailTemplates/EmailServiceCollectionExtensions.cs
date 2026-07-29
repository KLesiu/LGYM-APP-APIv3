using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Infrastructure.Configuration;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.Notifications;

internal static class EmailServiceCollectionExtensions
{
    internal static void ValidateEmailConfiguration(IConfiguration configuration)
    {
        EmailOptionsFactory.Validate(CreateEmailOptions(configuration));
    }

    internal static IServiceCollection AddEmailServices(this IServiceCollection services, IConfiguration configuration)
    {
        var emailOptions = CreateEmailOptions(configuration);
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

        return services;
    }

    private static EmailOptions CreateEmailOptions(IConfiguration configuration)
    {
        return EmailOptionsFactory.Create(configuration, configuration["AppDefaults:PreferredLanguage"]);
    }
}
