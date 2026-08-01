using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class EfExecuteUpdateUsageTests
{
    [Test]
    public void Codebase_ShouldNotUseEfExecuteUpdateAsync()
    {
        var root = FindRepoRoot();
        var offenders = new List<string>();

        var executeUpdateAsyncToken = "ExecuteUpdate" + "Async(";
        var setPropertyToken = "Set" + "Property(";

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, file);

            if (IsIgnoredPath(relativePath))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            if (content.Contains(executeUpdateAsyncToken, StringComparison.Ordinal)
                && content.Contains(setPropertyToken, StringComparison.Ordinal))
            {
                offenders.Add(relativePath);
            }
        }

        offenders.Should().BeEmpty("Found forbidden ExecuteUpdateAsync usage (SetProperty). Use the custom extension instead: " + string.Join(", ", offenders));
    }

    private static bool IsIgnoredPath(string relativePath)
    {
        var separator = Path.DirectorySeparatorChar;

        if (relativePath.EndsWith($"Extensions{separator}ExecuteUpdateExtensions.cs", StringComparison.Ordinal))
        {
            return true;
        }

        if (relativePath.Equals(
                Path.Combine("LgymApi.Notifications", "Persistence", "NotificationStageUpdateExtensions.cs"),
                StringComparison.Ordinal))
        {
            return true;
        }

        return relativePath.Contains($"{separator}bin{separator}")
            || relativePath.Contains($"{separator}obj{separator}");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
