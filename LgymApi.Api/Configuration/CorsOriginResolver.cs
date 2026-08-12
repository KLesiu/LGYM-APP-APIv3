namespace LgymApi.Api.Configuration;

public static class CorsOriginResolver
{
    private const string E2EAllowedOrigin = "http://localhost:8083";
    private const string E2EConfigurationError = "E2E CORS allowed origins configuration is invalid.";
    private static readonly string[] DevelopmentFallbackOrigins =
    [
        "http://localhost:3000",
        "http://127.0.0.1:3000",
        "http://localhost:5173",
        "http://127.0.0.1:5173"
    ];

    public static string[] ResolveAllowedOrigins(IEnumerable<string>? configuredOrigins, bool isDevelopment)
    {
        var normalizedOrigins = Normalize(configuredOrigins);

        return ResolveConfiguredOrFallback(normalizedOrigins, isDevelopment);
    }

    public static string[] ResolveAllowedOrigins(
        IEnumerable<string>? configuredOrigins,
        string environmentName)
    {
        var normalizedOrigins = Normalize(configuredOrigins);
        if (ApiEnvironmentNames.IsE2E(environmentName))
        {
            if (normalizedOrigins.Length != 1 ||
                !string.Equals(normalizedOrigins[0], E2EAllowedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(E2EConfigurationError);
            }

            return [E2EAllowedOrigin];
        }

        var isDevelopment = string.Equals(
            environmentName,
            Environments.Development,
            StringComparison.OrdinalIgnoreCase);
        return ResolveConfiguredOrFallback(normalizedOrigins, isDevelopment);
    }

    private static string[] Normalize(IEnumerable<string>? configuredOrigins) =>
        configuredOrigins?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string[] ResolveConfiguredOrFallback(string[] normalizedOrigins, bool isDevelopment)
    {
        if (normalizedOrigins.Length > 0)
        {
            return normalizedOrigins;
        }

        return isDevelopment ? DevelopmentFallbackOrigins.ToArray() : [];
    }
}
