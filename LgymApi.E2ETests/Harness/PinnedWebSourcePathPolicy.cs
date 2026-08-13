namespace LgymApi.E2ETests.Harness;

internal static class PinnedWebSourcePathPolicy
{
    internal static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) ||
            path.Contains('\0') ||
            path.Contains(':') ||
            path.Contains('\\') ||
            Path.IsPathRooted(path) ||
            Path.IsPathFullyQualified(path))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrEmpty(segment) ||
                segment is "." or ".." ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                IsWindowsDeviceName(segment)))
        {
            return false;
        }

        return !string.Equals(segments[0], ".git", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        var baseName = segment.Split('.')[0];
        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               IsNumberedDevice(baseName, "COM") ||
               IsNumberedDevice(baseName, "LPT");
    }

    private static bool IsNumberedDevice(string value, string prefix) =>
        value.Length == prefix.Length + 1 &&
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        value[^1] is >= '1' and <= '9';

    internal static string ResolveDestination(string destinationRoot, string path)
    {
        if (!IsSafeRelativePath(path))
        {
            throw new InvalidOperationException(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage);
        }

        var resolved = Path.GetFullPath(Path.Combine(destinationRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!PrivateRunDirectoryLayout.IsDescendantOrSame(destinationRoot, resolved))
        {
            throw new InvalidOperationException(PinnedWebSourceArchiveExtractor.ArchiveValidationMessage);
        }

        return resolved;
    }

    internal static void EnsureNoWindowsCollisions(IEnumerable<string> paths, string validationMessage)
    {
        var normalizedSegments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var current = string.Empty;
            var segments = path.Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                current = current.Length == 0 ? segment : $"{current}/{segment}";
                if (normalizedSegments.TryGetValue(current, out var existing) &&
                    !string.Equals(existing, current, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(validationMessage);
                }

                normalizedSegments[current] = current;
                if (index < segments.Length - 1)
                {
                    directoryPaths.Add(current);
                }
            }

            filePaths.Add(path);
        }

        if (filePaths.Overlaps(directoryPaths))
        {
            throw new InvalidOperationException(validationMessage);
        }
    }
}
