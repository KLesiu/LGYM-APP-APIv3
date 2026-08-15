using System.Security.Cryptography;
using System.Text.Json;
using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Lifecycle;

internal sealed class ScenarioFailureArtifactWriter
{
    internal const string FileName = "scenario-failure.json";
    internal const string FailureCategory = "scenario-callback-failure";
    internal const int MaximumArtifactBytes = 4096;

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
    {
        "scenario-paths",
        "postgresql",
        "external-api-host",
        "expo",
        "browser-run",
        "browser-scenario"
    };

    private readonly ApiPublicationReceipt _publication;
    private readonly IScenarioFailureArtifactFileSystem _fileSystem;
    private readonly int _maximumArtifactBytes;

    internal ScenarioFailureArtifactWriter(
        ApiPublicationReceipt publication,
        IScenarioFailureArtifactFileSystem? fileSystem = null,
        int maximumArtifactBytes = MaximumArtifactBytes)
    {
        _publication = publication;
        _fileSystem = fileSystem ?? new ScenarioFailureArtifactFileSystem();
        _maximumArtifactBytes = maximumArtifactBytes;
    }

    internal async Task WriteAsync(
        LifecycleScenarioDirectoryLease scenario,
        ScenarioLifecycleReceipt receipt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateRequest(scenario, receipt);

        var content = Serialize(scenario.CaseId, receipt);
        if (content.Length > _maximumArtifactBytes)
        {
            throw new InvalidOperationException("E2E scenario failure artifact exceeds its byte limit.");
        }

        var destinationPath = Path.Combine(scenario.ArtifactDirectory, FileName);
        var temporaryPath = Path.Combine(
            scenario.ArtifactDirectory,
            $".{FileName}.{RandomNumberGenerator.GetHexString(16, lowercase: true)}.tmp");
        try
        {
            scenario.EnsureSafeFailureArtifact(scenario.ArtifactDirectory);
            scenario.EnsureSafeFailureArtifact(destinationPath);
            scenario.EnsureSafeFailureArtifact(temporaryPath);
            await _fileSystem.WriteAsync(temporaryPath, content, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            scenario.EnsureSafeFailureArtifact(temporaryPath);
            scenario.EnsureSafeFailureArtifact(destinationPath);
            _fileSystem.Move(temporaryPath, destinationPath);
            scenario.EnsureSafeFailureArtifact(destinationPath);
        }
        finally
        {
            if (_fileSystem.FileExists(temporaryPath))
            {
                scenario.EnsureSafeFailureArtifact(temporaryPath);
                _fileSystem.DeleteFile(temporaryPath);
            }
        }
    }

    private byte[] Serialize(string caseId, ScenarioLifecycleReceipt receipt)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("caseId", caseId);
            json.WriteString("failureCategory", FailureCategory);
            json.WriteString("apiHeadSha", _publication.ApiRepositoryHeadSha);
            json.WriteBoolean("apiRepositoryDirty", _publication.RepositoryIsDirty);
            WriteCategories(json, "acquiredCategories", receipt.AcquiredCategories);
            WriteCategories(json, "cleanupCategories", receipt.AttemptedCleanupCategories);
            json.WriteNumber("cleanupFailureCount", receipt.CleanupFailureCount);
            json.WriteBoolean("databaseIdentityDistinct", receipt.DatabaseIdentityDistinct);
            json.WriteBoolean("previousResourcesAbsent", receipt.PreviousResourcesAbsent);
            json.WriteBoolean("browserStorageEmpty", receipt.BrowserStorageEmpty);
            json.WriteBoolean("databaseAbsent", receipt.DatabaseAbsent);
            json.WriteBoolean("apiAbsent", receipt.ApiAbsent);
            json.WriteBoolean("expoAbsent", receipt.ExpoAbsent);
            json.WriteBoolean("scenarioPathsAbsent", receipt.ScenarioPathsAbsent);
            json.WriteEndObject();
        }

        return stream.ToArray();
    }

    private void ValidateRequest(LifecycleScenarioDirectoryLease scenario, ScenarioLifecycleReceipt receipt)
    {
        PrivateRunDirectoryLease.EnsureCanonicalLifecycleId(scenario.CaseId);
        if (_maximumArtifactBytes <= 0 ||
            !IsSha(_publication.ApiRepositoryHeadSha) ||
            receipt.CleanupFailureCount is < 0 or > 1024 ||
            !CategoriesAreSafe(receipt.AcquiredCategories) ||
            !CategoriesAreSafe(receipt.AttemptedCleanupCategories))
        {
            throw new InvalidOperationException("E2E scenario failure artifact is invalid.");
        }
    }

    private static void WriteCategories(Utf8JsonWriter json, string propertyName, IReadOnlyList<string> categories)
    {
        json.WriteStartArray(propertyName);
        foreach (var category in categories)
        {
            json.WriteStringValue(category);
        }

        json.WriteEndArray();
    }

    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool CategoriesAreSafe(IReadOnlyList<string> categories) =>
        categories.Count <= 12 && categories.All(AllowedCategories.Contains);
}

internal interface IScenarioFailureArtifactFileSystem
{
    Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken);

    void Move(string sourcePath, string destinationPath);

    bool FileExists(string path);

    void DeleteFile(string path);
}

internal sealed class ScenarioFailureArtifactFileSystem : IScenarioFailureArtifactFileSystem
{
    public async Task WriteAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public void Move(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path) => File.Delete(path);
}
