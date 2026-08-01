using System.Xml.Linq;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NotificationsFcmProviderOwnershipGuardTests
{
    private static readonly string[] ExpectedFcmFiles =
    [
        "FcmPushSender.cs",
        "PushInstallationCleanupSettings.cs",
        "PushNotificationDeliveryRetrySettings.cs",
        "PushNotificationOptions.cs",
        "PushNotificationOptionsFactory.cs"
    ];

    [Test]
    public void FcmProviderImplementationAndConfiguration_ArePrivateNotificationsSource()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var providerDirectory = Path.Combine(repoRoot, "LgymApi.Notifications", "Providers", "Fcm");
        var providerFiles = Directory.GetFiles(providerDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(fileName => fileName, StringComparer.Ordinal)
            .ToArray();
        var senderSource = File.ReadAllText(Path.Combine(providerDirectory, "FcmPushSender.cs"));
        var optionsSource = File.ReadAllText(Path.Combine(providerDirectory, "PushNotificationOptions.cs"));
        var infrastructureDirectory = Path.Combine(repoRoot, "LgymApi.Infrastructure");

        Assert.Multiple(() =>
        {
            Assert.That(providerFiles, Is.EqualTo(ExpectedFcmFiles.OrderBy(fileName => fileName, StringComparer.Ordinal)));
            Assert.That(senderSource, Does.Contain("internal sealed class FcmPushSender"));
            Assert.That(senderSource, Does.Not.Contain("public sealed class FcmPushSender"));
            Assert.That(optionsSource, Does.Contain("internal sealed class PushNotificationOptions"));
            Assert.That(Directory.GetFiles(infrastructureDirectory, "*Fcm*.cs", SearchOption.AllDirectories), Is.Empty);
            Assert.That(File.Exists(Path.Combine(infrastructureDirectory, "Options", "PushNotificationOptions.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(infrastructureDirectory, "Configuration", "PushNotificationOptionsFactory.cs")), Is.False);
        });
    }

    [Test]
    public void FcmProviderPackagesAndRegistration_AreOwnedByNotificationsOnly()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var notificationsProject = LoadPackageReferences(Path.Combine(repoRoot, "LgymApi.Notifications", "LgymApi.Notifications.csproj"));
        var infrastructureProject = LoadPackageReferences(Path.Combine(repoRoot, "LgymApi.Infrastructure", "LgymApi.Infrastructure.csproj"));
        var notificationsSource = File.ReadAllText(Path.Combine(repoRoot, "LgymApi.Notifications", "ServiceCollectionExtensions.cs"));
        var infrastructureSource = File.ReadAllText(Path.Combine(repoRoot, "LgymApi.Infrastructure", "NotificationsServiceCollectionExtensions.cs"));

        Assert.Multiple(() =>
        {
            Assert.That(notificationsProject, Does.Contain("Google.Apis.Auth"));
            Assert.That(notificationsProject, Does.Contain("Microsoft.Extensions.Http"));
            Assert.That(infrastructureProject, Does.Not.Contain("Google.Apis.Auth"));
            Assert.That(infrastructureProject, Does.Not.Contain("Microsoft.Extensions.Http"));
            Assert.That(notificationsSource, Does.Contain("AddHttpClient(nameof(FcmPushSender))"));
            Assert.That(notificationsSource, Does.Contain("AddScoped<IPushProviderSender, FcmPushSender>()"));
            Assert.That(notificationsSource, Does.Not.Contain("IPushBackgroundScheduler"));
            Assert.That(infrastructureSource, Does.Not.Contain("IPushProviderSender"));
            Assert.That(infrastructureSource, Does.Not.Contain("FcmPushSender"));
            Assert.That(infrastructureSource, Does.Not.Contain("PushNotificationOptions"));
        });
    }

    [Test]
    public void FcmProviderLoggingAndPublicContract_ExcludeSensitiveProviderData()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var senderSource = File.ReadAllText(Path.Combine(repoRoot, "LgymApi.Notifications", "Providers", "Fcm", "FcmPushSender.cs"));
        var providerContractSource = File.ReadAllText(Path.Combine(repoRoot, "LgymApi.Notifications", "Contracts", "Push", "IPushProviderSender.cs"));
        var logStart = senderSource.IndexOf("_logger.LogWarning(", StringComparison.Ordinal);
        var logEnd = senderSource.IndexOf("return new PushSendAttemptResult(", logStart, StringComparison.Ordinal);
        var logInvocation = senderSource[logStart..logEnd];

        Assert.Multiple(() =>
        {
            Assert.That(logStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(logEnd, Is.GreaterThan(logStart));
            Assert.That(logInvocation, Does.Not.Contain("FcmToken"));
            Assert.That(logInvocation, Does.Not.Contain("Credentials"));
            Assert.That(logInvocation, Does.Not.Contain("providerResponse"));
            Assert.That(providerContractSource, Does.Not.Contain("Fcm"));
            Assert.That(providerContractSource, Does.Not.Contain("FcmToken"));
            Assert.That(providerContractSource, Does.Not.Contain("DeviceToken"));
            Assert.That(providerContractSource, Does.Not.Contain("RegistrationToken"));
            Assert.That(providerContractSource, Does.Not.Contain("Credentials"));
        });
    }

    private static string[] LoadPackageReferences(string projectPath)
    {
        return XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(packageName => packageName != null)
            .Cast<string>()
            .ToArray();
    }
}
