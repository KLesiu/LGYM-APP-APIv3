namespace LgymApi.E2ETests.Harness;

internal static class EnvironmentVariableScope
{
    public static void Run(IReadOnlyDictionary<string, string?> variables, Action assertion)
    {
        var originals = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (var (name, value) in variables)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            assertion();
        }
        finally
        {
            foreach (var (name, value) in originals)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            foreach (var (name, value) in originals)
            {
                Assert.That(Environment.GetEnvironmentVariable(name), Is.EqualTo(value), $"Environment variable '{name}' was not restored.");
            }
        }
    }
}
