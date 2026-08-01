using System.Reflection;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using Microsoft.CodeAnalysis;

namespace LgymApi.ArchitectureTests;

[TestFixture]
public sealed class NotificationPushContractArchitectureTests
{
    private const string ContractNamespace = "LgymApi.Application.Notifications.Contracts.Push";
    private const string ContractRelativePath = "LgymApi.Notifications/Contracts/Push";

    private static readonly string[] ExpectedFiles =
    [
        "IPushBackgroundScheduler.cs",
        "IPushNotificationDeliveryRetrySettings.cs",
        "IPushProviderSender.cs",
        "PushEventPayload.cs",
        "PushSendAttemptResult.cs"
    ];

    [Test]
    public void ApplicationPushContracts_MustLiveInNotificationsOwnedPathWithOneNamespaceRoot()
    {
        var repoRoot = ArchitectureTestHelpers.ResolveRepositoryRoot();
        var contractDirectory = Path.Combine(repoRoot, ContractRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.That(Directory.Exists(contractDirectory), Is.True, $"Missing Notifications push contract directory '{ContractRelativePath}'.");
        var sourceFiles = Directory.GetFiles(contractDirectory, "*.cs", SearchOption.AllDirectories);

        Assert.That(sourceFiles.Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(ExpectedFiles));
        Assert.That(
            sourceFiles.Select(path => Path.GetRelativePath(contractDirectory, path)),
            Is.All.Not.Contains(Path.DirectorySeparatorChar.ToString()),
            "All push contracts must remain in the single Notifications contract namespace root.");

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            Assert.That(source, Does.Contain($"namespace {ContractNamespace};"), $"'{sourceFile}' must use the single Notifications push contract namespace.");
        }
    }

    [Test]
    public void ApplicationPushContracts_MustExposeOnlyDomainTypesAndPreserveTypedIdException()
    {
        var notificationsAssembly = typeof(LgymApi.Notifications.Contracts.NotificationReference).Assembly;
        var contractTypes = notificationsAssembly.GetExportedTypes()
            .Where(type => type.Namespace == ContractNamespace)
            .ToArray();
        var payloadType = contractTypes.Single(type => type.Name == "PushEventPayload");
        var payloadProperties = payloadType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Multiple(() =>
        {
            Assert.That(contractTypes, Has.Length.EqualTo(6));
            Assert.That(contractTypes, Is.All.Matches<Type>(type => type.Assembly == notificationsAssembly && type.IsPublic));
            Assert.That(payloadProperties.Single(property => property.Name == "EntityId").PropertyType, Is.EqualTo(typeof(string)));
            Assert.That(
                payloadProperties.Single(property => property.Name == "InAppNotificationId").PropertyType,
                Is.EqualTo(typeof(Id<InAppNotification>?)));
        });

        var forbiddenExposures = contractTypes
            .SelectMany(GetExposedTypes)
            .Select(type => type.FullName ?? type.Name)
            .Where(name => name.Contains("LgymApi.BackgroundWorker", StringComparison.Ordinal)
                || name.Contains("Hangfire", StringComparison.Ordinal)
                || name.Contains("LgymApi.Infrastructure", StringComparison.Ordinal))
            .ToArray();

        Assert.That(forbiddenExposures, Is.Empty);
    }

    [Test]
    public void TypedEntityIdGuard_MustAllowOnlyTheApplicationPayloadEntityIdException()
    {
        var (_, compilation, syntaxTrees) = ArchitectureTestHelpers.PrepareCompilation(
            "LgymApi.Domain",
            "LgymApi.Application",
            "LgymApi.BackgroundWorker.Common");
        var violations = TypedEntityIdBoundaryGuard.Collect(compilation, syntaxTrees);
        var payloadType = compilation.GetTypeByMetadataName($"{ContractNamespace}.PushEventPayload");

        Assert.That(payloadType, Is.Not.Null);
        var entityId = payloadType!.GetMembers("EntityId").OfType<IPropertySymbol>().Single();
        var notificationId = payloadType.GetMembers("InAppNotificationId").OfType<IPropertySymbol>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(
                violations.Any(violation => SymbolEqualityComparer.Default.Equals(violation.Symbol, entityId)),
                Is.False,
                "PushEventPayload.EntityId is the exact polymorphic string exception.");
            Assert.That(
                violations.Any(violation => SymbolEqualityComparer.Default.Equals(violation.Symbol, notificationId)),
                Is.False,
                "PushEventPayload.InAppNotificationId must remain Id<InAppNotification>?.");
        });
    }

    private static IEnumerable<Type> GetExposedTypes(Type contractType)
    {
        yield return contractType;

        foreach (var property in contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            yield return property.PropertyType;
        }

        foreach (var constructor in contractType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in contractType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
