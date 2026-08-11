namespace LgymApi.E2ETests.Harness;

public static class RepositoryRoot
{
    private const string MainSolutionName = "LgymApi.sln";
    private const string StandaloneSolutionName = "LgymApi.E2ETests.sln";

    public static string Find()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, MainSolutionName)) &&
                File.Exists(Path.Combine(current.FullName, StandaloneSolutionName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Unable to locate repository root containing '{MainSolutionName}' and '{StandaloneSolutionName}'.");
    }
}
