using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications.Models;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Options;
using LgymApi.Infrastructure.Services;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class DummyEmailSenderTests
{
    private string _tempDirectory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"lgym-dummy-email-{Id<DummyEmailSenderTests>.New():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Test]
    public async Task SendAsync_WritesEmailToConfiguredDirectory()
    {
        var sender = new DummyEmailSender(new EmailOptions
        {
            Enabled = true,
            DeliveryMode = EmailDeliveryMode.Dummy,
            DummyOutputDirectory = _tempDirectory,
            FromAddress = "coach@example.com",
            FromName = "Coach"
        });

        var result = await sender.SendAsync(new EmailMessage
        {
            To = "trainee@example.com",
            Subject = "Invitation",
            Body = "Body content"
        });

        result.Should().BeTrue();
        var files = Directory.GetFiles(_tempDirectory, "*.email.txt");
        files.Length.Should().Be(1);

        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("To: trainee@example.com");
        content.Should().Contain("Subject: Invitation");
        content.Should().Contain("Body content");
    }

    [Test]
    public async Task SendAsync_ReturnsFalse_WhenEmailFeatureDisabled()
    {
        var sender = new DummyEmailSender(new EmailOptions
        {
            Enabled = false,
            DeliveryMode = EmailDeliveryMode.Dummy,
            DummyOutputDirectory = _tempDirectory
        });

        var result = await sender.SendAsync(new EmailMessage
        {
            To = "trainee@example.com",
            Subject = "x",
            Body = "y"
        });

        result.Should().BeFalse();
        Directory.Exists(_tempDirectory).Should().BeFalse();
    }
}
