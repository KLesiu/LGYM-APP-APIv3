using LgymApi.E2ETests.Configuration;

namespace LgymApi.E2ETests.Harness;

internal sealed record ExternalApiHostLaunchRequest(
    ApiPublication Publication,
    E2EOptions Options,
    string DotNetExecutable,
    IApiHostRuntimeLease Runtime,
    Uri BaseAddress)
{
    internal string EnvironmentName { get; init; } = "E2E";

    internal IReadOnlyList<string> SecretCanaries { get; init; } = [];
}

internal static class ExternalApiHostLaunchRequestFactory
{
    internal static ExternalProcessRequest Create(ExternalApiHostLaunchRequest request) => new()
    {
        FileName = request.DotNetExecutable,
        Arguments = [request.Publication.DllPath],
        WorkingDirectory = request.Publication.PublicationDirectory,
        EnvironmentVariables = CreateIsolatedEnvironment(request),
        ClearEnvironment = true,
        SecretCanaries = request.SecretCanaries,
        ExecutionTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.TestSessionSeconds),
        ShutdownTimeout = TimeSpan.FromSeconds(request.Options.Timeouts.ProcessShutdownSeconds)
    };

    private static IReadOnlyDictionary<string, string?> CreateIsolatedEnvironment(
        ExternalApiHostLaunchRequest request)
    {
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        var windowsDirectory = Environment.GetEnvironmentVariable("WINDIR");
        if (string.IsNullOrWhiteSpace(systemRoot) || string.IsNullOrWhiteSpace(windowsDirectory))
        {
            throw new ExternalApiHostStartupException(ExternalApiHostLease.StartupFailureMessage);
        }

        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = systemRoot,
            ["WINDIR"] = windowsDirectory,
            ["TEMP"] = request.Runtime.PrivateTempDirectory,
            ["TMP"] = request.Runtime.PrivateTempDirectory,
            ["ASPNETCORE_ENVIRONMENT"] = request.EnvironmentName,
            ["DOTNET_ENVIRONMENT"] = request.EnvironmentName,
            ["ASPNETCORE_URLS"] = request.BaseAddress.GetLeftPart(UriPartial.Authority),
            ["LGYM_APP_CONFIG_PATH"] = request.Runtime.ConfigurationPath,
            ["DOTNET_NOLOGO"] = "1",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        };
    }
}
