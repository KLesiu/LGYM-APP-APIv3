using System.Diagnostics;
using System.Text.Json;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Browser;

[TestFixture]
[Category("Task6Browser")]
[Category("WebHarness")]
public sealed class BrowserRunPlaywrightChromiumInstallerTests
{
    [TestCase("Debug")]
    [TestCase("Release")]
    public async Task Installer_invokes_built_package_script_for_Chromium_with_exact_private_path(string configuration)
    {
        using var fixture = new InstallerFixture(configuration, includePlaywrightScript: true);

        var result = await fixture.RunAsync();
        using var receipt = JsonDocument.Parse(File.ReadAllText(fixture.InvocationReceiptPath));
        var expectedBrowserRoot = Path.GetFullPath(Path.Combine(fixture.RepositoryRoot, ".e2e-private", "browsers"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(receipt.RootElement.GetProperty("browserPath").GetString(), Is.EqualTo(expectedBrowserRoot));
            Assert.That(receipt.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()),
                Is.EqualTo(new[] { "install", "chromium" }));
            Assert.That(File.ReadAllText(fixture.RestoredEnvironmentPath), Is.EqualTo("task-6-original"));
            Assert.That(result.Output, Does.Not.Contain(expectedBrowserRoot));
        });
    }

    [Test]
    public async Task Installer_rejects_invalid_configuration_without_invoking_Playwright()
    {
        using var fixture = new InstallerFixture("Checked", includePlaywrightScript: true);

        var result = await fixture.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("Configuration must be Debug or Release."));
            Assert.That(File.Exists(fixture.InvocationReceiptPath), Is.False);
        });
    }

    [Test]
    public async Task Installer_requires_the_built_package_matched_Playwright_script()
    {
        using var fixture = new InstallerFixture("Release", includePlaywrightScript: false);
        var privateBrowserRoot = Path.GetFullPath(Path.Combine(fixture.RepositoryRoot, ".e2e-private", "browsers"));

        var result = await fixture.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Output, Does.Contain("Build the E2E project before installing Chromium."));
            Assert.That(result.Output, Does.Not.Contain(privateBrowserRoot));
            Assert.That(File.Exists(fixture.InvocationReceiptPath), Is.False);
        });
    }
}

internal sealed record InstallerResult(int ExitCode, string Output);

internal sealed class InstallerFixture : IDisposable
{
    private const string FakePlaywrightScript = """
        $receipt = @{
            browserPath = $env:PLAYWRIGHT_BROWSERS_PATH
            arguments = @($args)
        } | ConvertTo-Json -Compress
        Set-Content -LiteralPath $env:TASK6_INVOCATION_RECEIPT -Value $receipt -NoNewline
        Write-Output $env:PLAYWRIGHT_BROWSERS_PATH
        exit 0
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("lgym-task6-installer-").FullName;
    private readonly string _configuration;
    private readonly string _wrapperPath;

    internal InstallerFixture(string configuration, bool includePlaywrightScript)
    {
        _configuration = configuration;
        RepositoryRoot = Path.Combine(_root, "repository");
        var projectRoot = Path.Combine(RepositoryRoot, "LgymApi.E2ETests");
        var scriptsRoot = Path.Combine(projectRoot, "scripts");
        Directory.CreateDirectory(scriptsRoot);
        InstallerPath = Path.Combine(scriptsRoot, "install-playwright-chromium.ps1");
        File.Copy(
            Path.Combine(RepositoryRootFinder(), "LgymApi.E2ETests", "scripts", "install-playwright-chromium.ps1"),
            InstallerPath);
        InvocationReceiptPath = Path.Combine(_root, "invocation.json");
        RestoredEnvironmentPath = Path.Combine(_root, "restored.txt");
        _wrapperPath = Path.Combine(_root, "invoke.ps1");

        if (includePlaywrightScript)
        {
            var outputRoot = Path.Combine(projectRoot, "bin", configuration, "net10.0");
            Directory.CreateDirectory(outputRoot);
            File.WriteAllText(Path.Combine(outputRoot, "playwright.ps1"), FakePlaywrightScript);
        }

        File.WriteAllText(_wrapperPath, CreateWrapperScript());
    }

    internal string RepositoryRoot { get; }
    internal string InstallerPath { get; }
    internal string InvocationReceiptPath { get; }
    internal string RestoredEnvironmentPath { get; }

    internal async Task<InstallerResult> RunAsync()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(_wrapperPath);
        startInfo.Environment["TASK6_INVOCATION_RECEIPT"] = InvocationReceiptPath;

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("PowerShell did not start.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        return new InstallerResult(process.ExitCode, await standardOutput + await standardError);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CreateWrapperScript() => $$"""
        $env:PLAYWRIGHT_BROWSERS_PATH = 'task-6-original'
        try {
            & '{{Escape(InstallerPath)}}' -Configuration '{{Escape(_configuration)}}'
        }
        catch {
            [Console]::Error.WriteLine($_.Exception.Message)
            exit 1
        }
        finally {
            Set-Content -LiteralPath '{{Escape(RestoredEnvironmentPath)}}' -Value $env:PLAYWRIGHT_BROWSERS_PATH -NoNewline
        }
        """;

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string RepositoryRootFinder() => Harness.RepositoryRoot.Find();
}
