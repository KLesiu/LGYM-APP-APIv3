namespace LgymApi.E2ETests.Harness;

internal sealed class WebSourceRunEnvironment(NodeNpmTools tools, string gitExecutable)
{
    private readonly string _nodeDirectory = Path.GetDirectoryName(tools.NodeExecutable)!;
    private readonly string _gitDirectory = Path.GetDirectoryName(gitExecutable)!;

    internal Dictionary<string, string?> Create(
        string runDirectory,
        string npmCacheDirectory)
    {
        var homeDirectory = Path.Combine(runDirectory, "npm-home");
        var temporaryDirectory = Path.Combine(runDirectory, "npm-temp");
        var applicationDataDirectory = Path.Combine(runDirectory, "npm-app-data");
        var localApplicationDataDirectory = Path.Combine(runDirectory, "npm-local-app-data");
        Directory.CreateDirectory(homeDirectory);
        Directory.CreateDirectory(temporaryDirectory);
        Directory.CreateDirectory(applicationDataDirectory);
        Directory.CreateDirectory(localApplicationDataDirectory);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? string.Empty;
        return new Dictionary<string, string?>
        {
            ["SystemRoot"] = systemRoot,
            ["WINDIR"] = Environment.GetEnvironmentVariable("WINDIR") ?? systemRoot,
            ["ComSpec"] = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(systemRoot, "System32", "cmd.exe"),
            ["HOME"] = homeDirectory,
            ["USERPROFILE"] = homeDirectory,
            ["APPDATA"] = applicationDataDirectory,
            ["LOCALAPPDATA"] = localApplicationDataDirectory,
            ["TEMP"] = temporaryDirectory,
            ["TMP"] = temporaryDirectory,
            ["PATH"] = string.Join(Path.PathSeparator, _nodeDirectory, _gitDirectory, Path.Combine(systemRoot, "System32")),
            ["CI"] = "1",
            ["NO_COLOR"] = "1",
            ["npm_config_cache"] = npmCacheDirectory,
            ["npm_config_userconfig"] = Path.Combine(homeDirectory, ".npmrc"),
            ["npm_config_audit"] = "false",
            ["npm_config_fund"] = "false",
            ["npm_config_update_notifier"] = "false",
            ["npm_config_progress"] = "false",
            ["npm_config_loglevel"] = "warn"
        };
    }

    internal Dictionary<string, string?> CreateScenarioRuntime(
        string runtimeDirectory,
        string npmCacheDirectory) =>
        Create(runtimeDirectory, npmCacheDirectory);
}
