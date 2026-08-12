using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
public sealed class FinalTrxManifestTests
{
    [Test]
    public void Final_evidence_test_emits_manifest_from_explicit_raw_trx_and_output_paths()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var testResultsRoot = Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults");
        var temporaryDirectory = Path.Combine(testResultsRoot, $"final-manifest-{Guid.NewGuid():N}");
        var rawTrxPath = Path.Combine(temporaryDirectory, "input.trx");
        var manifestPath = Path.Combine(temporaryDirectory, "manifest.json");
        const string rawTrx = """
            <TestRun runUser="leaked-user" computerName="leaked-machine">
              <ResultSummary>
                <Counters total="1" executed="1" passed="1" failed="0" timeout="0" notExecuted="0" />
              </ResultSummary>
              <Results>
                <UnitTestResult testName="Synthetic_proof" outcome="Passed" />
              </Results>
            </TestRun>
            """;

        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllText(rawTrxPath, rawTrx);

            FinalTrxManifestSerializer.Write(rawTrxPath, manifestPath);

            var outsidePath = Path.Combine(repositoryRoot, "outside-final-manifest.json");
            var exception = Assert.Throws<ArgumentException>(() => FinalTrxManifestSerializer.Write(rawTrxPath, outsidePath));

            var serialized = File.ReadAllText(manifestPath);
            using var document = JsonDocument.Parse(serialized);
            Assert.Multiple(() =>
            {
                Assert.That(exception!.Message, Does.Contain("under the E2E TestResults directory"));
                Assert.That(Path.GetRelativePath(repositoryRoot, manifestPath), Does.StartWith(Path.Combine("LgymApi.E2ETests", "TestResults")));
                Assert.That(document.RootElement.GetProperty("counters").GetProperty("passed").GetInt32(), Is.EqualTo(1));
                Assert.That(document.RootElement.GetProperty("tests")[0].GetProperty("testName").GetString(), Is.EqualTo("Synthetic_proof"));
                Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(["counters", "tests"]));
                Assert.That(serialized, Does.Not.Contain("leaked-user"));
                Assert.That(serialized, Does.Not.Contain("leaked-machine"));
                Assert.That(serialized, Does.Not.Contain(rawTrxPath));
                Assert.That(serialized, Does.Not.Contain(manifestPath));
            });
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void Serialize_projects_raw_TRX_to_counters_and_test_outcomes_only()
    {
        const string rawTrx = """
            <TestRun runUser="leaked-user" computerName="leaked-machine">
              <TestDefinitions>
                <UnitTest storage="C:\private\checkout\proof.dll">
                  <TestMethod codeBase="C:\private\published\proof.dll" />
                </UnitTest>
              </TestDefinitions>
              <Deployment runDeploymentRoot="C:\private\deployment" />
              <ResultSummary>
                <Counters total="2" executed="2" passed="1" failed="1" timeout="0" notExecuted="0" />
              </ResultSummary>
              <Results>
                <UnitTestResult testName="Expected_proof" outcome="Passed" />
                <UnitTestResult testName="Expected_failure" outcome="Failed" />
              </Results>
            </TestRun>
            """;

        var manifest = FinalTrxManifestSerializer.Parse(rawTrx);
        var serialized = FinalTrxManifestSerializer.Serialize(manifest);
        using var document = JsonDocument.Parse(serialized);

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Counters, Is.EqualTo(new FinalTrxCounters(2, 2, 1, 1, 0, 0)));
            Assert.That(manifest.Tests, Does.Contain(new FinalTrxTestOutcome("Expected_proof", "Passed")));
            Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(["counters", "tests"]));
            Assert.That(serialized, Does.Not.Contain("leaked-user"));
            Assert.That(serialized, Does.Not.Contain("leaked-machine"));
            Assert.That(serialized, Does.Not.Contain(@"C:\private\checkout\proof.dll"));
            Assert.That(serialized, Does.Not.Contain(@"C:\private\published\proof.dll"));
            Assert.That(serialized, Does.Not.Contain(@"C:\private\deployment"));
            Assert.That(serialized, Does.Contain("Expected_proof"));
            Assert.That(serialized, Does.Contain("Passed"));
        });
    }

    [Test]
    public void Final_canonical_TRX_emits_passed_manifest_without_raw_metadata()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var testResultsRoot = Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults");
        var rawTrxPath = Path.Combine(testResultsRoot, "issue-433-api-host.trx");
        var manifestPath = Path.Combine(testResultsRoot, "issue-433-api-host.manifest.json");

        File.Delete(manifestPath);
        FinalTrxManifestSerializer.Write(rawTrxPath, manifestPath);

        var serialized = File.ReadAllText(manifestPath);
        using var document = JsonDocument.Parse(serialized);
        var counters = document.RootElement.GetProperty("counters");
        var tests = document.RootElement.GetProperty("tests");
        var outcomes = tests.EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(counters.GetProperty("total").GetInt32(), Is.GreaterThan(0));
            Assert.That(counters.GetProperty("executed").GetInt32(), Is.EqualTo(counters.GetProperty("total").GetInt32()));
            Assert.That(counters.GetProperty("passed").GetInt32(), Is.EqualTo(counters.GetProperty("total").GetInt32()));
            Assert.That(counters.GetProperty("failed").GetInt32(), Is.Zero);
            Assert.That(counters.GetProperty("timeout").GetInt32(), Is.Zero);
            Assert.That(counters.GetProperty("notExecuted").GetInt32(), Is.Zero);
            Assert.That(outcomes, Is.Not.Empty);
            Assert.That(outcomes.All(test => test.GetProperty("outcome").GetString() == "Passed"), Is.True);
            Assert.That(outcomes.Select(test => test.GetProperty("testName").GetString()),
                Does.Contain("E2E_fresh_PostgreSQL_is_migrated_before_database_backed_readiness"));
            Assert.That(serialized, Does.Not.Contain("runUser"));
            Assert.That(serialized, Does.Not.Contain("computerName"));
            Assert.That(serialized, Does.Not.Contain("storage"));
            Assert.That(serialized, Does.Not.Contain("codeBase"));
            Assert.That(serialized, Does.Not.Contain(".e2e-private/runs"));
            Assert.That(serialized, Does.Not.Contain(":\\"));
        });
    }
}
