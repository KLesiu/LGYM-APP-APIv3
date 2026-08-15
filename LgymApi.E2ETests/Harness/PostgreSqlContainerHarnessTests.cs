using LgymApi.E2ETests.Lifecycle;
using System.Reflection;
using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class PostgreSqlContainerHarnessTests
{
    [Test]
    public void PostgreSQL_observation_contract_is_non_disposable_and_redacted()
    {
        var observationType = typeof(PostgreSqlContainerLease).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.ScenarioResourceObservation");
        var identityType = typeof(PostgreSqlContainerLease).Assembly.GetType(
            "LgymApi.E2ETests.Lifecycle.ScenarioResourceIdentity");

        Assert.Multiple(() =>
        {
            Assert.That(typeof(PostgreSqlContainerLease).GetMethod(
                "CreateObservation",
                BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(observationType, Is.Not.Null);
            Assert.That(identityType, Is.Not.Null);
            Assert.That(observationType!.GetInterfaces(), Does.Not.Contain(typeof(IAsyncDisposable)));
            Assert.That(observationType.GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null);
            Assert.That(observationType.GetMethod("ConfirmAbsentAsync", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(identityType!.GetMethod("ToString", BindingFlags.Instance | BindingFlags.Public), Is.Not.Null);
        });
    }

    [Test]
    [Category("HarnessDocker")]
    public async Task PostgreSQL_container_starts_with_module_readiness_and_is_removed_on_disposal()
    {
        PostgreSqlContainerLease? lease = null;

        try
        {
            lease = await PostgreSqlContainerLease.StartAsync();
            var observation = lease.CreateObservation();
            var repeatedObservation = lease.CreateObservation();

            Assert.Multiple(() =>
            {
                Assert.That(lease.IsRunning, Is.True);
                Assert.That(observation.Identity, Is.EqualTo(repeatedObservation.Identity));
                Assert.That(observation.Identity.ToString(), Is.EqualTo("<scenario-resource-identity>"));
                Assert.That(observation.ToString(), Is.EqualTo("<scenario-resource-observation>"));
            });
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }

        Assert.That(lease!.CleanupReceipt.ContainerAbsent, Is.True);
        Assert.That(await lease.ConfirmAbsentAsync(), Is.True);
        Assert.That(await lease.CreateObservation().ConfirmAbsentAsync(), Is.True);
        WriteCleanupReceipt(lease);
    }

    [Test]
    [Category("HarnessDocker")]
    public void PostgreSQL_container_is_removed_when_a_test_local_failure_occurs_after_start()
    {
        PostgreSqlContainerLease? lease = null;

        var exception = Assert.ThrowsAsync<InjectedPostStartFailureException>(async () =>
        {
            lease = await PostgreSqlContainerLease.StartAsync();
            try
            {
                throw new InjectedPostStartFailureException();
            }
            finally
            {
                await lease.DisposeAsync();
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<InjectedPostStartFailureException>());
            Assert.That(exception!.Message, Is.EqualTo("Injected post-start lifecycle failure."));
            Assert.That(lease, Is.Not.Null);
            Assert.That(lease!.CleanupReceipt.ContainerAbsent, Is.True);
        });
        WriteCleanupReceipt(lease!);
    }

    [Test]
    [Category("HarnessDocker")]
    public async Task PostgreSQL_post_container_start_callback_failure_proves_private_locator_absence()
    {
        string? containerId = null;
        var exception = Assert.ThrowsAsync<InjectedStartupCallbackFailure>(() => PostgreSqlContainerLease.StartAsync(
            (container, _) =>
            {
                containerId = container.Id;
                return Task.FromException(new InjectedStartupCallbackFailure());
            }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Injected post-container startup failure."));
            Assert.That(containerId, Is.Not.Null.And.Not.Empty);
        });

        Assert.That(
            await DockerContainerProbe.WaitUntilAbsentAsync(containerId!, TimeSpan.FromSeconds(30)),
            Is.True);
    }

    [Test]
    [Category("HarnessDocker")]
    public async Task PostgreSQL_sequential_leases_have_distinct_redacted_observations_and_are_absent()
    {
        var first = await PostgreSqlContainerLease.StartAsync();
        var firstObservation = first.CreateObservation();
        await first.DisposeAsync();

        var second = await PostgreSqlContainerLease.StartAsync();
        var secondObservation = second.CreateObservation();
        await second.DisposeAsync();
        var firstAbsent = await firstObservation.ConfirmAbsentAsync();
        var secondAbsent = await secondObservation.ConfirmAbsentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(firstObservation.Identity, Is.Not.EqualTo(secondObservation.Identity));
            Assert.That(firstObservation.Identity.ToString(), Is.EqualTo("<scenario-resource-identity>"));
            Assert.That(secondObservation.Identity.ToString(), Is.EqualTo("<scenario-resource-identity>"));
            Assert.That(firstAbsent, Is.True);
            Assert.That(secondAbsent, Is.True);
        });

        WriteEvidenceReceipt(new HarnessDockerEvidenceReceipt(
            TestCount: 1,
            PassedCount: 1,
            AllContainersAbsent: firstAbsent && secondAbsent,
            IdentitiesDistinct: !firstObservation.Identity.Equals(secondObservation.Identity),
            RawIdentitiesExcluded: true));
    }

    private static void WriteCleanupReceipt(PostgreSqlContainerLease lease)
    {
        TestContext.Out.WriteLine(
            $"Task 4 PostgreSQL cleanup: category={lease.CleanupReceipt.Category}; absent={lease.CleanupReceipt.ContainerAbsent}; durationMilliseconds={(long)lease.CleanupReceipt.Duration.TotalMilliseconds}.");
    }

    private static void WriteEvidenceReceipt(HarnessDockerEvidenceReceipt receipt)
    {
        var serialized = HarnessDockerEvidenceReceiptWriter.Write(receipt);
        TestContext.Out.WriteLine(serialized);
    }

    private sealed class InjectedPostStartFailureException : Exception
    {
        public InjectedPostStartFailureException()
            : base("Injected post-start lifecycle failure.")
        {
        }
    }

    private sealed class InjectedStartupCallbackFailure : Exception
    {
        public InjectedStartupCallbackFailure()
            : base("Injected post-container startup failure.")
        {
        }
    }
}

internal sealed record HarnessDockerEvidenceReceipt(
    int TestCount,
    int PassedCount,
    bool AllContainersAbsent,
    bool IdentitiesDistinct,
    bool RawIdentitiesExcluded);

internal static class HarnessDockerEvidenceReceiptWriter
{
    internal const string ReceiptPathEnvironmentVariable = "HARNESS_ONLY_HARNESS_DOCKER_RECEIPT_PATH";

    internal static string Write(HarnessDockerEvidenceReceipt receipt)
    {
        var serialized = Serialize(receipt);
        var configuredPath = Environment.GetEnvironmentVariable(ReceiptPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return serialized;
        }

        var repositoryRoot = RepositoryRoot.Find();
        var testResultsRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults"));
        var receiptPath = Path.GetFullPath(configuredPath);
        var relativePath = Path.GetRelativePath(testResultsRoot, receiptPath);
        if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HarnessDocker evidence receipt path is invalid.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(receiptPath)!);
        File.WriteAllText(receiptPath, serialized);
        return serialized;
    }

    internal static string Serialize(HarnessDockerEvidenceReceipt receipt)
    {
        if (receipt.TestCount <= 0 || receipt.PassedCount != receipt.TestCount ||
            !receipt.AllContainersAbsent || !receipt.IdentitiesDistinct || !receipt.RawIdentitiesExcluded)
        {
            throw new InvalidOperationException("HarnessDocker evidence receipt is incomplete.");
        }

        var serialized = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        ValidateSerializedReceipt(serialized);
        return serialized;
    }

    internal static void ValidateSerializedReceipt(string serialized)
    {
        using var document = JsonDocument.Parse(serialized);
        var expectedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "testCount", "passedCount", "allContainersAbsent", "identitiesDistinct", "rawIdentitiesExcluded"
        };
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            document.RootElement.EnumerateObject().Count() != expectedNames.Count ||
            document.RootElement.EnumerateObject().Any(property => !expectedNames.Contains(property.Name)) ||
            expectedNames.Any(name => !document.RootElement.TryGetProperty(name, out _)) ||
            document.RootElement.GetProperty("testCount").ValueKind != JsonValueKind.Number ||
            document.RootElement.GetProperty("passedCount").ValueKind != JsonValueKind.Number ||
            expectedNames.Where(name => name is not "testCount" and not "passedCount")
                .Any(name => document.RootElement.GetProperty(name).ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
        {
            throw new InvalidOperationException("HarnessDocker evidence receipt contains unsafe fields.");
        }
    }
}
