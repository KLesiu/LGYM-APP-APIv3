using System.Security.Cryptography;
using System.Text.Json;
using LgymApi.E2ETests.Lifecycle;

namespace LgymApi.E2ETests.Harness;

internal enum ApiRuntimeConfigurationProfile
{
    E2E,
    SyntheticCloudflareR2
}

internal sealed record ApiRuntimeDatabase(string ConnectionString);

internal sealed record RuntimeConfigurationRequest(
    PrivateRunDirectoryRequest Directory,
    ApiRuntimeDatabase Database,
    ApiRuntimeConfigurationProfile Profile)
{
    internal IReadOnlyList<string>? CorsAllowedOrigins { get; init; }

    internal LifecycleComponentDirectoryLease? ApiRuntimeDirectory { get; init; }
}

internal sealed record RuntimeConfigurationFileWriteRequest(
    string Path,
    byte[] Content,
    Action<string> EnsureSafeRuntimeArtifact);

internal interface IRuntimeConfigurationFileWriter
{
    Task WriteAsync(RuntimeConfigurationFileWriteRequest request, CancellationToken cancellationToken);
}

internal sealed class RuntimeConfigurationInfrastructure(
    IRuntimeConfigurationFileWriter fileWriter,
    IRunDirectoryCleaner directoryCleaner)
{
    internal IRuntimeConfigurationFileWriter FileWriter { get; } = fileWriter;

    internal IRunDirectoryCleaner DirectoryCleaner { get; } = directoryCleaner;

    internal static RuntimeConfigurationInfrastructure CreateDefault() => new(
        new AtomicRuntimeConfigurationFileWriter(),
        new FileSystemRunDirectoryCleaner());
}

internal sealed class RuntimeConfigurationLease : IAsyncDisposable
{
    private const string ConfigurationFileName = "appsettings.e2e.json";
    private readonly PrivateRunDirectoryLease? _directoryLease;
    private readonly LifecycleComponentDirectoryLease? _apiRuntimeDirectory;
    private readonly Action<string> _ensureSafeRuntimeArtifact;

    private RuntimeConfigurationLease(PrivateRunDirectoryLease directoryLease)
    {
        _directoryLease = directoryLease;
        RunDirectory = directoryLease.RunDirectory;
        ConfigurationPath = Path.Combine(RunDirectory, "api", ConfigurationFileName);
        _ensureSafeRuntimeArtifact = directoryLease.EnsureSafeRuntimeArtifact;
    }

    private RuntimeConfigurationLease(LifecycleComponentDirectoryLease apiRuntimeDirectory)
    {
        _apiRuntimeDirectory = apiRuntimeDirectory;
        RunDirectory = apiRuntimeDirectory.ComponentDirectory;
        ConfigurationPath = Path.Combine(RunDirectory, ConfigurationFileName);
        _ensureSafeRuntimeArtifact = apiRuntimeDirectory.EnsureSafeArtifact;
    }

    internal string ConfigurationPath { get; }

    internal string RunDirectory { get; }

    internal string CreatePrivateTempDirectory()
    {
        var tempDirectory = _apiRuntimeDirectory is null
            ? Path.Combine(RunDirectory, "api", "temp")
            : Path.Combine(RunDirectory, "temp");
        _ensureSafeRuntimeArtifact(tempDirectory);
        Directory.CreateDirectory(tempDirectory);
        _ensureSafeRuntimeArtifact(tempDirectory);
        return tempDirectory;
    }

    internal static Task<RuntimeConfigurationLease> CreateAsync(
        RuntimeConfigurationRequest request,
        CancellationToken cancellationToken = default) =>
        CreateAsync(request, RuntimeConfigurationInfrastructure.CreateDefault(), cancellationToken);

    internal static async Task<RuntimeConfigurationLease> CreateAsync(
        RuntimeConfigurationRequest request,
        RuntimeConfigurationInfrastructure infrastructure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(infrastructure);
        if (string.IsNullOrWhiteSpace(request.Database.ConnectionString))
        {
            throw new InvalidOperationException("E2E runtime configuration input is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var directoryLease = PrivateRunDirectoryLease.Create(request.Directory, infrastructure.DirectoryCleaner);
        var lease = new RuntimeConfigurationLease(directoryLease);

        return await WriteAsync(request, lease, infrastructure, cancellationToken);
    }

    internal static Task<RuntimeConfigurationLease> CreateAsync(
        RuntimeConfigurationRequest request,
        LifecycleComponentDirectoryLease apiRuntimeDirectory,
        CancellationToken cancellationToken = default) =>
        CreateAsync(request, apiRuntimeDirectory, RuntimeConfigurationInfrastructure.CreateDefault(), cancellationToken);

    internal static async Task<RuntimeConfigurationLease> CreateAsync(
        RuntimeConfigurationRequest request,
        LifecycleComponentDirectoryLease apiRuntimeDirectory,
        RuntimeConfigurationInfrastructure infrastructure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(apiRuntimeDirectory);
        ArgumentNullException.ThrowIfNull(infrastructure);
        if (string.IsNullOrWhiteSpace(request.Database.ConnectionString))
        {
            throw new InvalidOperationException("E2E runtime configuration input is invalid.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var lease = new RuntimeConfigurationLease(apiRuntimeDirectory);
        return await WriteAsync(request, lease, infrastructure, cancellationToken);
    }

    private static async Task<RuntimeConfigurationLease> WriteAsync(
        RuntimeConfigurationRequest request,
        RuntimeConfigurationLease lease,
        RuntimeConfigurationInfrastructure infrastructure,
        CancellationToken cancellationToken)
    {

        lease._ensureSafeRuntimeArtifact(Path.GetDirectoryName(lease.ConfigurationPath)!);
        lease._ensureSafeRuntimeArtifact(lease.ConfigurationPath);
        try
        {
            var apiDirectory = Path.GetDirectoryName(lease.ConfigurationPath)!;
            Directory.CreateDirectory(apiDirectory);
            lease._ensureSafeRuntimeArtifact(apiDirectory);
            lease._ensureSafeRuntimeArtifact(lease.ConfigurationPath);
            await infrastructure.FileWriter.WriteAsync(
                new RuntimeConfigurationFileWriteRequest(
                    lease.ConfigurationPath,
                    ApiRuntimeConfigurationWriter.CreateJson(request),
                    lease._ensureSafeRuntimeArtifact),
                cancellationToken);
            return lease;
        }
        catch
        {
            if (lease._apiRuntimeDirectory is null)
            {
                await lease.DisposeAsync();
            }

            throw;
        }
    }

    public ValueTask DisposeAsync() => _apiRuntimeDirectory is null
        ? _directoryLease!.DisposeAsync()
        : _apiRuntimeDirectory.DisposeAsync();

    public override string ToString() => "<runtime-configuration-lease>";

}

internal static class ApiRuntimeConfigurationWriter
{
    internal static byte[] CreateJson(RuntimeConfigurationRequest request)
    {
        using var stream = new MemoryStream();
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteStartObject("ConnectionStrings");
        json.WriteString("Postgres", request.Database.ConnectionString);
        json.WriteEndObject();
        json.WriteStartObject("Jwt");
        json.WriteString("SigningKey", CreateCanary("jwt"));
        json.WriteEndObject();
        json.WriteStartObject("Cors");
        json.WriteStartArray("AllowedOrigins");
        foreach (var origin in request.CorsAllowedOrigins ?? ["http://localhost:8083"])
        {
            json.WriteStringValue(origin);
        }

        json.WriteEndArray();
        json.WriteEndObject();
        WritePhotoStorage(json, request.Profile);
        json.WriteStartObject("Email");
        json.WriteBoolean("Enabled", false);
        json.WriteEndObject();
        json.WriteStartObject("PushNotifications");
        json.WriteBoolean("Enabled", false);
        json.WriteBoolean("SendEnabled", false);
        json.WriteEndObject();
        json.WriteStartObject("Logging");
        json.WriteStartObject("LogLevel");
        json.WriteString("Microsoft.Hosting.Lifetime", "Information");
        json.WriteEndObject();
        json.WriteEndObject();
        json.WriteEndObject();
        json.Flush();
        return stream.ToArray();
    }

    private static void WritePhotoStorage(Utf8JsonWriter json, ApiRuntimeConfigurationProfile profile)
    {
        json.WriteStartObject("PhotoStorage");
        if (profile == ApiRuntimeConfigurationProfile.E2E)
        {
            json.WriteString("Provider", "Local");
            json.WriteString("LocalDevelopmentSigningKey", CreateCanary("photo"));
        }
        else
        {
            json.WriteString("Provider", "CloudflareR2");
            json.WriteString("BucketName", "lgym-report-photos-test");
            json.WriteString("AccountId", "synthetic-e2e-account");
            json.WriteString("Endpoint", "https://synthetic-e2e-account.r2.cloudflarestorage.com");
            json.WriteString("AccessKeyId", CreateCanary("r2-access"));
            json.WriteString("SecretAccessKey", CreateCanary("r2-secret"));
        }

        json.WriteEndObject();
    }

    private static string CreateCanary(string category) =>
        $"e2e-canary-{category}-{RandomNumberGenerator.GetHexString(64, lowercase: true)}";
}

internal sealed class AtomicRuntimeConfigurationFileWriter : IRuntimeConfigurationFileWriter
{
    public async Task WriteAsync(RuntimeConfigurationFileWriteRequest request, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(request.Path)
            ?? throw new InvalidOperationException("E2E runtime configuration destination is invalid.");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(request.Path)}.{RandomNumberGenerator.GetHexString(16, lowercase: true)}.tmp");

        try
        {
            request.EnsureSafeRuntimeArtifact(directory);
            request.EnsureSafeRuntimeArtifact(request.Path);
            request.EnsureSafeRuntimeArtifact(temporaryPath);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                request.EnsureSafeRuntimeArtifact(temporaryPath);
                await stream.WriteAsync(request.Content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            request.EnsureSafeRuntimeArtifact(temporaryPath);
            request.EnsureSafeRuntimeArtifact(request.Path);
            File.Move(temporaryPath, request.Path);
            request.EnsureSafeRuntimeArtifact(request.Path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
