namespace LgymApi.Api.Configuration;

public static class ApiEnvironmentNames
{
    public const string Testing = "Testing";
    public const string E2E = "E2E";

    public static bool IsTesting(string? environmentName) =>
        string.Equals(environmentName, Testing, StringComparison.OrdinalIgnoreCase);

    public static bool IsE2E(string? environmentName) =>
        string.Equals(environmentName, E2E, StringComparison.OrdinalIgnoreCase);

    public static bool IsTestSafe(string? environmentName) =>
        IsTesting(environmentName) || IsE2E(environmentName);
}
