using Microsoft.Extensions.Configuration;

namespace LgymApi.E2ETests.Configuration;

public static class E2EConfiguration
{
    public const string ConfigurationFileName = "appsettings.E2E.json";
    public const string EnvironmentVariablePrefix = "LGYM_";

    public static E2EOptions Load(string configurationDirectory, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(configurationDirectory)
            .AddJsonFile(ConfigurationFileName, optional: false, reloadOnChange: false)
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .Build();
        var section = configuration.GetRequiredSection(E2EOptions.SectionName);

        E2EOptionsValidator.ValidateSchema(section);

        E2EOptions options;
        try
        {
            options = section.Get<E2EOptions>(binderOptions => binderOptions.ErrorOnUnknownConfiguration = true)
                ?? throw new InvalidOperationException("Invalid E2E configuration: E2E section is required.");
        }
        catch (InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Invalid E2E configuration: configuration values must use the declared types.");
        }

        E2EOptionsValidator.Validate(options, repositoryRoot);
        return options;
    }
}
