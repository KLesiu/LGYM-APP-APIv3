using System.Globalization;
using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using LgymApi.Domain.Notifications;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class EmailTemplateComposerFactoryTests
{
    private string _templateRootPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _templateRootPath = Path.Combine(Path.GetTempPath(), $"lgym-email-template-factory-{Id<EmailTemplateComposerFactoryTests>.New():N}");
        Directory.CreateDirectory(_templateRootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_templateRootPath))
        {
            Directory.Delete(_templateRootPath, recursive: true);
        }
    }

    [Test]
    public void ComposeMessage_ThrowsForUnknownNotificationType()
    {
        var factory = new EmailTemplateComposerFactory(Array.Empty<IEmailTemplateComposer>());

        var action = () => factory.ComposeMessage(EmailNotificationType.Define("email.unknown"), "{}");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Email template composer not registered for notification type: email.unknown");
    }

    [Test]
    public void LoadTemplate_ThrowsWhenNoLocaleOrDefaultTemplateExists()
    {
        var composer = CreateComposer();

        var action = () => composer.Load("Missing", CultureInfo.GetCultureInfo("fr-FR"));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Email template not found: *");
    }

    [Test]
    public void LoadTemplate_ThrowsForMalformedTemplate()
    {
        var templateDirectory = Path.Combine(_templateRootPath, "Malformed");
        Directory.CreateDirectory(templateDirectory);
        File.WriteAllText(Path.Combine(templateDirectory, "en.email"), "Subject: Missing separator");
        var composer = CreateComposer();

        var action = () => composer.Load("Malformed", CultureInfo.GetCultureInfo("en-US"));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Invalid email template format in *");
    }

    private ExposedEmailTemplateComposer CreateComposer()
    {
        return new ExposedEmailTemplateComposer(new EmailOptions
        {
            TemplateRootPath = _templateRootPath,
            DefaultCulture = CultureInfo.GetCultureInfo("en-US")
        });
    }

    private sealed class ExposedEmailTemplateComposer(EmailOptions emailOptions) : EmailTemplateComposerBase(emailOptions)
    {
        public (string Subject, string Body) Load(string templateName, CultureInfo culture)
        {
            return LoadTemplate(templateName, culture);
        }
    }
}
