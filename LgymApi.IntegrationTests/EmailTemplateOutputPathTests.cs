using FluentAssertions;
using System.Security.Cryptography;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class EmailTemplateOutputPathTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedTemplates =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PasswordRecovery/en.email"] = "88e0160546ec42b9271c562cbeffe7385bcece92ee51b6dda0c6842d780aa8ff",
            ["PasswordRecovery/pl.email"] = "3d78fafbb040316326be01aeb891a44a0061e1054bfb1c9b8526f91b179cd739",
            ["TrainerInvitation/en.email"] = "16fce6f7a92dbcab05eb340be9dac6b1625964c91225615be560af805ca3fe30",
            ["TrainerInvitation/pl.email"] = "a0b1bd2efeac828218cbe00fd5a6ac42d971530c3386e476a585de3475d0838f",
            ["TrainerInvitationAccepted/en.email"] = "9ebcfbd4c08e5ce3ee7e53d162fab52d72b4db861e62d5e4ab17a8d692035f9a",
            ["TrainerInvitationAccepted/pl.email"] = "cdc0b8ae64d36b24c7e8879151f72f00c4a24182e9f1787dcfdff7f1f9d1ae5f",
            ["TrainerInvitationRevoked/en.email"] = "093ca6aff14dac3db946db803f18768ea1e1770db36eabe8e393e5fc6dffc982",
            ["TrainerInvitationRevoked/pl.email"] = "c49829ecc26a02e2aed45740fce6805f80bec017f95f01fa9c3143762bcd7ab8",
            ["TrainingCompleted/en.email"] = "e15a42e989f4fe190129867367fa1a2164e0010d2e93950a28de6bfd591326fe",
            ["TrainingCompleted/pl.email"] = "e2d749b4f0e801f9db231048116ac37151318962e18000d142ee2af2c92ba1e0",
            ["Welcome/en.email"] = "37a71e6f3b9ec94a01887216e12740e350442860e9c1786fcdf93234615cb982",
            ["Welcome/pl.email"] = "6d9b48387dbe16b146a042dd00c9a74bda06b1646dbab34f59e70934851025d5"
        };

    [Test]
    public void EmailTemplates_ArePublishedAtStableRuntimePaths()
    {
        var templateRoot = Path.Combine(AppContext.BaseDirectory, "EmailTemplates");

        Directory.Exists(templateRoot).Should().BeTrue();
        var publishedTemplatePaths = Directory.EnumerateFiles(templateRoot, "*.email", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(templateRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        publishedTemplatePaths.Should().Equal(ExpectedTemplates.Keys.OrderBy(path => path, StringComparer.Ordinal));
    }

    [Test]
    public void EmailTemplates_HaveStableHashesInSourceApiAndIntegrationOutputs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = outputDirectory.Name;
        var configuration = outputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Unable to resolve the test output configuration.");
        var templateRoots = new[]
        {
            Path.Combine(repositoryRoot, "LgymApi.Notifications", "EmailTemplates"),
            Path.Combine(repositoryRoot, "LgymApi.Api", "bin", configuration, targetFramework, "EmailTemplates"),
            Path.Combine(AppContext.BaseDirectory, "EmailTemplates")
        };

        foreach (var templateRoot in templateRoots)
        {
            ValidateManifest(ReadManifest(templateRoot));
        }
    }

    [Test]
    public void EmailTemplateManifest_RejectsMissingAndAlteredTemplateFixtures()
    {
        var missing = ExpectedTemplates
            .Where(entry => entry.Key != "Welcome/pl.email")
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        var altered = ExpectedTemplates.ToDictionary(
            entry => entry.Key,
            entry => entry.Key == "Welcome/pl.email" ? new string('0', 64) : entry.Value,
            StringComparer.Ordinal);

        var missingAction = () => ValidateManifest(missing);
        var alteredAction = () => ValidateManifest(altered);

        missingAction.Should().Throw<InvalidOperationException>()
            .WithMessage("The runtime email template manifest must contain exactly 12 files.");
        alteredAction.Should().Throw<InvalidOperationException>()
            .WithMessage("Runtime email template hash mismatch for 'Welcome/pl.email'.");
    }

    private static IReadOnlyDictionary<string, string> ReadManifest(string templateRoot)
    {
        if (!Directory.Exists(templateRoot))
        {
            throw new InvalidOperationException($"Runtime email template root is missing: {templateRoot}");
        }

        return Directory.EnumerateFiles(templateRoot, "*.email", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(templateRoot, path).Replace('\\', '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                StringComparer.Ordinal);
    }

    private static void ValidateManifest(IReadOnlyDictionary<string, string> actual)
    {
        if (actual.Count != ExpectedTemplates.Count)
        {
            throw new InvalidOperationException("The runtime email template manifest must contain exactly 12 files.");
        }

        foreach (var expected in ExpectedTemplates)
        {
            if (!actual.TryGetValue(expected.Key, out var actualHash))
            {
                throw new InvalidOperationException($"Runtime email template is missing: '{expected.Key}'.");
            }

            if (!string.Equals(actualHash, expected.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Runtime email template hash mismatch for '{expected.Key}'.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to locate the repository root from the test output directory.");
    }
}
