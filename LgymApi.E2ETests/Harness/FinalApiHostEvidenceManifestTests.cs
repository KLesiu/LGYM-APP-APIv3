using System.Text.Json;

namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("Harness")]
[Category("ApiHostProof")]
public sealed class SanitizedApiHostEvidenceManifestTests
{
    [Test]
    public void Final_manifest_preserves_required_proofs_and_safe_counters_without_raw_TRX_metadata()
    {
        var rawTrx = CreateSyntheticRawTrx();
        var manifest = SanitizedApiHostEvidenceManifest.Create(
            rawTrx,
            new FinalEvidenceRepositoryReceipt(new string('a', 40), true),
            [new("restore", true), new("build", true), new("publish", true), new("test", true)]);
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("counters").GetProperty("passed").GetInt32(), Is.EqualTo(SanitizedApiHostEvidenceManifest.RequiredProofNames.Length));
            Assert.That(root.GetProperty("proofs").GetArrayLength(), Is.EqualTo(SanitizedApiHostEvidenceManifest.RequiredProofNames.Length));
            Assert.That(root.GetProperty("commands").GetArrayLength(), Is.EqualTo(4));
            Assert.That(root.GetProperty("receipts").GetProperty("output").GetProperty("retainedUtf8ByteLimit").GetInt32(), Is.EqualTo(65536));
            Assert.That(root.GetProperty("receipts").GetProperty("negativeHost").GetProperty("processTreeAbsent").GetBoolean(), Is.True);
            Assert.That(manifest, Does.Not.Contain("raw-user-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-machine-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-path-canary"));
            Assert.That(manifest, Does.Not.Contain("raw-secret-canary"));
        });
    }

    [TestCase("Passed", true)]
    [TestCase("Failed", false)]
    public void Final_manifest_handles_duplicate_required_proof_outcomes(string duplicateOutcome, bool shouldCreate)
    {
        var rawTrx = CreateSyntheticRawTrx(duplicateCorsResult: true, duplicateOutcome: duplicateOutcome);
        var create = () => SanitizedApiHostEvidenceManifest.Create(
            rawTrx,
            new FinalEvidenceRepositoryReceipt(new string('a', 40), true),
            [new("restore", true), new("build", true), new("publish", true), new("test", true)]);

        if (!shouldCreate)
        {
            Assert.That(create, Throws.TypeOf<InvalidOperationException>());
            return;
        }

        using var document = JsonDocument.Parse(create());
        Assert.That(document.RootElement.GetProperty("proofs").GetArrayLength(), Is.EqualTo(SanitizedApiHostEvidenceManifest.RequiredProofNames.Length));
    }

    [Test]
    public void Final_manifest_requires_the_complete_passing_command_sequence()
    {
        var repository = new FinalEvidenceRepositoryReceipt(new string('b', 40), false);
        var commands = new[]
        {
            new FinalEvidenceCommandReceipt("restore", true),
            new FinalEvidenceCommandReceipt("build", true),
            new FinalEvidenceCommandReceipt("publish", true),
            new FinalEvidenceCommandReceipt("test", false)
        };

        Assert.That(
            () => SanitizedApiHostEvidenceManifest.Create(CreateSyntheticRawTrx(), repository, commands),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Final_manifest_writer_persists_only_the_sanitized_contract()
    {
        var directory = Directory.CreateTempSubdirectory("lgym-e2e-manifest-").FullName;
        var rawTrxPath = Path.Combine(directory, "raw.trx");
        File.WriteAllText(rawTrxPath, CreateSyntheticRawTrx());
        var path = Path.Combine(directory, SanitizedApiHostEvidenceManifest.ManifestFileName);
        var manifest = SanitizedApiHostEvidenceManifest.Create(
            File.ReadAllText(rawTrxPath),
            new FinalEvidenceRepositoryReceipt(new string('c', 40), false),
            [new("restore", true), new("build", true), new("publish", true), new("test", true)]);

        try
        {
            await Task.Run(() => SanitizedApiHostEvidenceManifest.Write(path, manifest));

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.ReadAllText(path), Does.Not.Contain("raw-path-canary"));
                Assert.That(File.ReadAllText(path), Does.Not.Contain("raw-secret-canary"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Final_manifest_is_current_complete_and_sanitized_for_the_canonical_run()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var rawTrxPath = Path.Combine(repositoryRoot, "LgymApi.E2ETests", "TestResults", "issue-433-api-host.trx");
        var manifestPath = Path.Combine(Path.GetDirectoryName(rawTrxPath)!, SanitizedApiHostEvidenceManifest.ManifestFileName);
        var commands = new FinalEvidenceCommandReceipt[]
        {
            new("restore", true),
            new("build", true),
            new("publish", true),
            new("test", true)
        };

        await SanitizedApiHostEvidenceManifest.WriteForCurrentRunAsync(rawTrxPath, commands);

        var manifest = File.ReadAllText(manifestPath);
        using var document = JsonDocument.Parse(manifest);
        var root = document.RootElement;
        var proofs = root.GetProperty("proofs").EnumerateArray().ToArray();
        var commandReceipts = root.GetProperty("commands").EnumerateArray().ToArray();
        var negativeHost = root.GetProperty("receipts").GetProperty("negativeHost");

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("issue-433-final-evidence-v1"));
            Assert.That(root.GetProperty("repository").GetProperty("HeadSha").GetString(), Has.Length.EqualTo(40));
            Assert.That(commandReceipts, Has.Length.EqualTo(4));
            Assert.That(commandReceipts.All(command => command.GetProperty("Passed").GetBoolean()), Is.True);
            Assert.That(root.GetProperty("counters").GetProperty("total").GetInt32(), Is.EqualTo(97));
            Assert.That(root.GetProperty("counters").GetProperty("executed").GetInt32(), Is.EqualTo(97));
            Assert.That(root.GetProperty("counters").GetProperty("passed").GetInt32(), Is.EqualTo(97));
            Assert.That(root.GetProperty("counters").GetProperty("failed").GetInt32(), Is.Zero);
            Assert.That(root.GetProperty("counters").GetProperty("notExecuted").GetInt32(), Is.Zero);
            Assert.That(proofs, Has.Length.EqualTo(SanitizedApiHostEvidenceManifest.RequiredProofNames.Length));
            Assert.That(proofs.Select(proof => proof.GetProperty("name").GetString()), Is.EquivalentTo(SanitizedApiHostEvidenceManifest.RequiredProofNames));
            Assert.That(proofs.All(proof => proof.GetProperty("outcome").GetString() == "Passed"), Is.True);
            Assert.That(negativeHost.GetProperty("ready").GetBoolean(), Is.False);
            Assert.That(negativeHost.GetProperty("processTreeAbsent").GetBoolean(), Is.True);
            Assert.That(negativeHost.GetProperty("privateRunAbsent").GetBoolean(), Is.True);
            Assert.That(negativeHost.GetProperty("containerAbsent").GetBoolean(), Is.True);
            Assert.That(root.GetProperty("receipts").GetProperty("output").GetProperty("retainedUtf8ByteLimit").GetInt32(), Is.EqualTo(65536));
            Assert.That(root.GetProperty("receipts").GetProperty("output").GetProperty("truncationRecorded").GetBoolean(), Is.True);
            Assert.That(manifest, Does.Not.Contain("runUser"));
            Assert.That(manifest, Does.Not.Contain("computerName"));
            Assert.That(manifest, Does.Not.Contain("storage"));
            Assert.That(manifest, Does.Not.Contain("codeBase"));
            Assert.That(manifest, Does.Not.Contain("deploymentRoot"));
            Assert.That(manifest, Does.Not.Contain("raw-"));
            Assert.That(manifest, Does.Not.Contain(".e2e-private"));
            Assert.That(manifest, Does.Not.Contain("C:\\"));
            Assert.That(manifest, Does.Not.Contain("ConnectionStrings"));
            Assert.That(manifest, Does.Not.Contain("Jwt"));
            Assert.That(manifest, Does.Not.Contain("Password"));
        });
    }

    private static string CreateSyntheticRawTrx(bool duplicateCorsResult = false, string duplicateOutcome = "Passed")
    {
        var results = string.Join(string.Empty, SanitizedApiHostEvidenceManifest.RequiredProofNames.Select(name =>
            $"<UnitTestResult testName=\"{name}\" outcome=\"Passed\" />"));
        if (duplicateCorsResult)
        {
            results += $"<UnitTestResult testName=\"E2E_rejects_broadened_CORS_configuration_before_readiness\" outcome=\"{duplicateOutcome}\" />";
        }

        return $"<TestRun runUser=\"raw-user-canary\" computerName=\"raw-machine-canary\"><TestDefinitions storage=\"raw-path-canary\" codeBase=\"raw-codebase-canary\" deploymentRoot=\"raw-deployment-canary\" /><ResultSummary><Counters total=\"{SanitizedApiHostEvidenceManifest.RequiredProofNames.Length}\" executed=\"{SanitizedApiHostEvidenceManifest.RequiredProofNames.Length}\" passed=\"{SanitizedApiHostEvidenceManifest.RequiredProofNames.Length}\" failed=\"0\" notExecuted=\"0\" /></ResultSummary><Results>{results}</Results><Output>raw-secret-canary</Output></TestRun>";
    }
}
