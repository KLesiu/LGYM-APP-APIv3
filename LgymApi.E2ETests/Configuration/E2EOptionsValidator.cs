using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace LgymApi.E2ETests.Configuration;

public static class E2EOptionsValidator
{
    private const string RepositoryUrl = "https://github.com/KLesiu/LGYM-APP-MOBILE.git";
    internal const string PinnedCommitSha = "8f59d96ec368f509b1565e3296cd89d2a082a952";
    private const string DatabaseImage = "postgres:17.10-alpine3.24";
    private const string DatabaseNamePrefix = "lgym_e2e";
    private static readonly Regex CommitShaPattern = new(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant);

    public static void ValidateSchema(IConfigurationSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var errors = new List<string>();
        if (section.Value is not null)
        {
            errors.Add("schema contains an unsupported setting.");
        }

        ValidateSection(section, "WebSource", ["RepositoryUrl", "CommitSha"], ["SourcePath"], errors);
        ValidateSection(section, "Api", ["PublishedDllPath", "Port"], [], errors);
        ValidateSection(section, "Web", ["Port"], [], errors);
        ValidateSection(section, "Runtime", ["PrivateRunRoot"], [], errors);
        ValidateSection(section, "Database", ["Image", "NamePrefix"], [], errors);
        ValidateSection(
            section,
            "Timeouts",
            [
                "ContainerStartupSeconds",
                "ApiPublishSeconds",
                "ApiStartupSeconds",
                "WebStartupSeconds",
                "ProcessShutdownSeconds",
                "HttpRequestSeconds",
                "BrowserActionMilliseconds",
                "ScenarioSeconds",
                "TestSessionSeconds"
            ],
            [],
            errors);

        foreach (var child in section.GetChildren().OrderBy(child => child.Key, StringComparer.Ordinal))
        {
            if (child.Key is not ("WebSource" or "Api" or "Web" or "Runtime" or "Database" or "Timeouts"))
            {
                errors.Add("schema contains an unsupported setting.");
            }
        }

        ThrowIfInvalid(errors);
    }

    public static void Validate(E2EOptions options, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var errors = new List<string>();
        var normalizedRepositoryRoot = Path.GetFullPath(repositoryRoot);
        var webSource = options.WebSource;

        if (!IsCanonicalRepositoryUrl(webSource.RepositoryUrl))
        {
            errors.Add("WebSource.RepositoryUrl must be the canonical credential-free HTTPS repository URL.");
        }

        if (!CommitShaPattern.IsMatch(webSource.CommitSha ?? string.Empty))
        {
            errors.Add("WebSource.CommitSha must be a lowercase 40-character hexadecimal SHA.");
        }
        else if (!string.Equals(webSource.CommitSha, PinnedCommitSha, StringComparison.Ordinal))
        {
            errors.Add("WebSource.CommitSha must match the configured immutable mobile source SHA.");
        }

        if (!IsExternalSourcePath(webSource.SourcePath, normalizedRepositoryRoot))
        {
            errors.Add("WebSource.SourcePath must be absent or an absolute path outside this repository.");
        }

        if (!IsSafePrivateRelativePath(options.Api.PublishedDllPath))
        {
            errors.Add("Api.PublishedDllPath must be a safe relative path under .e2e-private.");
        }

        if (options.Api.Port is not 0 and (< 1024 or > 65535))
        {
            errors.Add("Api.Port must be 0 or between 1024 and 65535.");
        }

        if (options.Web.Port != 8083)
        {
            errors.Add("Web.Port must be 8083.");
        }

        if (!IsSafePrivateRelativePath(options.Runtime.PrivateRunRoot))
        {
            errors.Add("Runtime.PrivateRunRoot must be a safe relative path under .e2e-private.");
        }

        if (!string.Equals(options.Database.Image, DatabaseImage, StringComparison.Ordinal))
        {
            errors.Add("Database.Image must be postgres:17.10-alpine3.24.");
        }

        if (!string.Equals(options.Database.NamePrefix, DatabaseNamePrefix, StringComparison.Ordinal))
        {
            errors.Add("Database.NamePrefix must be lgym_e2e.");
        }

        ValidateTimeouts(options.Timeouts, errors);
        ThrowIfInvalid(errors);
    }

    private static void ValidateSection(
        IConfigurationSection root,
        string sectionName,
        string[] requiredKeys,
        string[] optionalKeys,
        List<string> errors)
    {
        var section = root.GetSection(sectionName);
        if (!section.Exists())
        {
            errors.Add($"schema is missing required section: E2E.{sectionName}.");
            return;
        }

        if (section.Value is not null)
        {
            errors.Add("schema contains an unsupported setting.");
        }

        var allowedKeys = requiredKeys.Concat(optionalKeys).ToHashSet(StringComparer.Ordinal);
        foreach (var requiredKey in requiredKeys)
        {
            if (string.IsNullOrWhiteSpace(section[requiredKey]))
            {
                errors.Add($"schema is missing required setting: E2E.{sectionName}.{requiredKey}.");
            }
        }

        foreach (var child in section.GetChildren().OrderBy(child => child.Key, StringComparer.Ordinal))
        {
            if (!allowedKeys.Contains(child.Key) || child.GetChildren().Any())
            {
                errors.Add("schema contains an unsupported setting.");
            }
        }
    }

    private static bool IsCanonicalRepositoryUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) &&
               string.IsNullOrEmpty(uri.UserInfo) &&
               string.Equals(value, RepositoryUrl, StringComparison.Ordinal);
    }

    private static bool IsExternalSourcePath(string? sourcePath, string repositoryRoot)
    {
        if (sourcePath is null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(sourcePath) || !Path.IsPathFullyQualified(sourcePath))
        {
            return false;
        }

        try
        {
            var normalizedSourcePath = Path.GetFullPath(sourcePath);
            var normalizedRepositoryRoot = Path.TrimEndingDirectorySeparator(repositoryRoot);
            var repositoryPrefix = normalizedRepositoryRoot + Path.DirectorySeparatorChar;

            return !string.Equals(normalizedSourcePath, normalizedRepositoryRoot, StringComparison.OrdinalIgnoreCase) &&
                   !normalizedSourcePath.StartsWith(repositoryPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSafePrivateRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 1 &&
               string.Equals(segments[0], ".e2e-private", StringComparison.Ordinal) &&
               segments.All(segment => segment is not "." and not "..");
    }

    private static void ValidateTimeouts(E2ETimeoutsOptions timeouts, List<string> errors)
    {
        ValidateRange(timeouts.ContainerStartupSeconds, 1, 600, "Timeouts.ContainerStartupSeconds", "seconds", errors);
        ValidateRange(timeouts.ApiPublishSeconds, 1, 600, "Timeouts.ApiPublishSeconds", "seconds", errors);
        ValidateRange(timeouts.ApiStartupSeconds, 1, 600, "Timeouts.ApiStartupSeconds", "seconds", errors);
        ValidateRange(timeouts.WebStartupSeconds, 1, 600, "Timeouts.WebStartupSeconds", "seconds", errors);
        ValidateRange(timeouts.ProcessShutdownSeconds, 1, 120, "Timeouts.ProcessShutdownSeconds", "seconds", errors);
        ValidateRange(timeouts.HttpRequestSeconds, 1, 300, "Timeouts.HttpRequestSeconds", "seconds", errors);
        ValidateRange(timeouts.BrowserActionMilliseconds, 100, 120000, "Timeouts.BrowserActionMilliseconds", "milliseconds", errors);
        ValidateRange(timeouts.ScenarioSeconds, 1, 1800, "Timeouts.ScenarioSeconds", "seconds", errors);
        ValidateRange(timeouts.TestSessionSeconds, 1, 3600, "Timeouts.TestSessionSeconds", "seconds", errors);
    }

    private static void ValidateRange(int value, int minimum, int maximum, string setting, string unit, List<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"{setting} must be between {minimum} and {maximum} {unit}.");
        }
    }

    private static void ThrowIfInvalid(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Invalid E2E configuration: " + string.Join(" ", errors));
        }
    }
}
