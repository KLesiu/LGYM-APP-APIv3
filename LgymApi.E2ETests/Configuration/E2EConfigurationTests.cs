using LgymApi.E2ETests.Harness;

namespace LgymApi.E2ETests.Configuration;

[TestFixture]
[Category("Harness")]
[Category("Configuration")]
[NonParallelizable]
public sealed class E2EConfigurationTests
{
    private const string SourcePathVariable = "LGYM_E2E__WebSource__SourcePath";
    private const string HttpTimeoutVariable = "LGYM_E2E__Timeouts__HttpRequestSeconds";

    [Test]
    public void Committed_safe_configuration_loads_with_exact_defaults()
    {
        var options = LoadOutputConfiguration();

        Assert.Multiple(() =>
        {
            Assert.That(options.WebSource.RepositoryUrl, Is.EqualTo("https://github.com/KLesiu/LGYM-APP-MOBILE.git"));
            Assert.That(options.WebSource.CommitSha, Is.EqualTo("8f59d96ec368f509b1565e3296cd89d2a082a952"));
            Assert.That(options.WebSource.SourcePath, Is.Null);
            Assert.That(options.Api.PublishedDllPath, Is.EqualTo(".e2e-private/published-api/LgymApi.Api.dll"));
            Assert.That(options.Api.Port, Is.Zero);
            Assert.That(options.Web.Port, Is.EqualTo(8083));
            Assert.That(options.Runtime.PrivateRunRoot, Is.EqualTo(".e2e-private/runs"));
            Assert.That(options.Database.Image, Is.EqualTo("postgres:17.10-alpine3.24"));
            Assert.That(options.Database.NamePrefix, Is.EqualTo("lgym_e2e"));
            Assert.That(options.Timeouts.ContainerStartupSeconds, Is.EqualTo(120));
            Assert.That(options.Timeouts.ApiStartupSeconds, Is.EqualTo(120));
            Assert.That(options.Timeouts.WebStartupSeconds, Is.EqualTo(120));
            Assert.That(options.Timeouts.ProcessShutdownSeconds, Is.EqualTo(15));
            Assert.That(options.Timeouts.HttpRequestSeconds, Is.EqualTo(30));
            Assert.That(options.Timeouts.BrowserActionMilliseconds, Is.EqualTo(15000));
            Assert.That(options.Timeouts.ScenarioSeconds, Is.EqualTo(180));
            Assert.That(options.Timeouts.TestSessionSeconds, Is.EqualTo(900));
        });
    }

    [Test]
    public void LGYM_environment_overrides_JSON_without_requiring_external_paths_to_exist()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "lgym-e2e-missing-" + Path.GetRandomFileName());
        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SourcePathVariable] = sourcePath,
            [HttpTimeoutVariable] = "31",
            ["E2E__Web__Port"] = "9999"
        };

        WithEnvironmentVariables(variables, () =>
        {
            Assert.That(Directory.Exists(sourcePath), Is.False);

            var options = LoadOutputConfiguration();

            Assert.Multiple(() =>
            {
                Assert.That(options.WebSource.SourcePath, Is.EqualTo(sourcePath));
                Assert.That(options.Timeouts.HttpRequestSeconds, Is.EqualTo(31));
                Assert.That(options.Web.Port, Is.EqualTo(8083));
            });
        });
    }

    [TestCase("LGYM_E2E__Unexpected")]
    [TestCase("LGYM_E2E")]
    [TestCase("LGYM_E2E__WebSource")]
    public void Unsupported_LGYM_setting_fails_with_a_sanitized_deterministic_diagnostic(string variableName)
    {
        const string injectedValue = "unsupported-scalar";
        WithEnvironmentVariables(
            new Dictionary<string, string?> { [variableName] = injectedValue },
            () =>
            {
                var exception = Assert.Throws<InvalidOperationException>(() => LoadOutputConfiguration());

                Assert.Multiple(() =>
                {
                    Assert.That(
                        exception!.Message,
                        Is.EqualTo("Invalid E2E configuration: schema contains an unsupported setting."));
                    Assert.That(exception.Message, Does.Not.Contain(injectedValue));
                });
            });
    }

    [Test]
    public void Invalid_environment_value_type_fails_without_echoing_the_value()
    {
        WithEnvironmentVariables(
            new Dictionary<string, string?> { [HttpTimeoutVariable] = "not-an-integer" },
            () =>
            {
                var exception = Assert.Throws<InvalidOperationException>(() => LoadOutputConfiguration());

                Assert.Multiple(() =>
                {
                    Assert.That(
                        exception!.Message,
                        Is.EqualTo("Invalid E2E configuration: configuration values must use the declared types."));
                    Assert.That(exception.Message, Does.Not.Contain("not-an-integer"));
                });
            });
    }

    [TestCase("invalid repository URL", "WebSource.RepositoryUrl must be the canonical credential-free HTTPS repository URL.")]
    [TestCase("invalid commit", "WebSource.CommitSha must be a lowercase 40-character hexadecimal SHA.")]
    [TestCase("repository source path", "WebSource.SourcePath must be absent or an absolute path outside this repository.")]
    [TestCase("published DLL traversal", "Api.PublishedDllPath must be a safe relative path under .e2e-private.")]
    [TestCase("API port", "Api.Port must be 0 or between 1024 and 65535.")]
    [TestCase("web port", "Web.Port must be 8083.")]
    [TestCase("run root traversal", "Runtime.PrivateRunRoot must be a safe relative path under .e2e-private.")]
    [TestCase("database image", "Database.Image must be postgres:17.10-alpine3.24.")]
    [TestCase("database prefix", "Database.NamePrefix must be lgym_e2e.")]
    public void Invalid_schema_boundaries_fail_with_sanitized_deterministic_diagnostics(string caseName, string expectedError)
    {
        var options = CreateValidOptions();
        ApplyInvalidCase(options, caseName);

        AssertValidationFailure(options, expectedError);
    }

    [TestCase(nameof(E2ETimeoutsOptions.ContainerStartupSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.ContainerStartupSeconds), 601)]
    [TestCase(nameof(E2ETimeoutsOptions.ApiStartupSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.ApiStartupSeconds), 601)]
    [TestCase(nameof(E2ETimeoutsOptions.WebStartupSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.WebStartupSeconds), 601)]
    [TestCase(nameof(E2ETimeoutsOptions.ProcessShutdownSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.ProcessShutdownSeconds), 121)]
    [TestCase(nameof(E2ETimeoutsOptions.HttpRequestSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.HttpRequestSeconds), 301)]
    [TestCase(nameof(E2ETimeoutsOptions.BrowserActionMilliseconds), 99)]
    [TestCase(nameof(E2ETimeoutsOptions.BrowserActionMilliseconds), 120001)]
    [TestCase(nameof(E2ETimeoutsOptions.ScenarioSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.ScenarioSeconds), 1801)]
    [TestCase(nameof(E2ETimeoutsOptions.TestSessionSeconds), 0)]
    [TestCase(nameof(E2ETimeoutsOptions.TestSessionSeconds), 3601)]
    public void Invalid_timeout_boundaries_fail_with_the_exact_range_diagnostic(string timeoutName, int value)
    {
        var options = CreateValidOptions();
        SetTimeout(options.Timeouts, timeoutName, value);

        AssertValidationFailure(options, ExpectedTimeoutError(timeoutName));
    }

    private static E2EOptions LoadOutputConfiguration() => E2EConfiguration.Load(
        TestContext.CurrentContext.TestDirectory,
        RepositoryRoot.Find());

    private static E2EOptions CreateValidOptions() => new()
    {
        WebSource = new E2EWebSourceOptions
        {
            RepositoryUrl = "https://github.com/KLesiu/LGYM-APP-MOBILE.git",
            CommitSha = "8f59d96ec368f509b1565e3296cd89d2a082a952"
        },
        Api = new E2EApiOptions { PublishedDllPath = ".e2e-private/published-api/LgymApi.Api.dll", Port = 0 },
        Web = new E2EWebOptions { Port = 8083 },
        Runtime = new E2ERuntimeOptions { PrivateRunRoot = ".e2e-private/runs" },
        Database = new E2EDatabaseOptions { Image = "postgres:17.10-alpine3.24", NamePrefix = "lgym_e2e" },
        Timeouts = new E2ETimeoutsOptions
        {
            ContainerStartupSeconds = 120,
            ApiStartupSeconds = 120,
            WebStartupSeconds = 120,
            ProcessShutdownSeconds = 15,
            HttpRequestSeconds = 30,
            BrowserActionMilliseconds = 15000,
            ScenarioSeconds = 180,
            TestSessionSeconds = 900
        }
    };

    private static void ApplyInvalidCase(E2EOptions options, string caseName)
    {
        switch (caseName)
        {
            case "invalid repository URL":
                options.WebSource.RepositoryUrl = "https://invalid@github.com/KLesiu/LGYM-APP-MOBILE.git";
                break;
            case "invalid commit":
                options.WebSource.CommitSha = new string('A', 40);
                break;
            case "repository source path":
                options.WebSource.SourcePath = Path.Combine(RepositoryRoot.Find(), "external-source");
                break;
            case "published DLL traversal":
                options.Api.PublishedDllPath = ".e2e-private/../LgymApi.Api.dll";
                break;
            case "API port":
                options.Api.Port = 1023;
                break;
            case "web port":
                options.Web.Port = 8082;
                break;
            case "run root traversal":
                options.Runtime.PrivateRunRoot = ".e2e-private/../runs";
                break;
            case "database image":
                options.Database.Image = "postgres:17";
                break;
            case "database prefix":
                options.Database.NamePrefix = "lgym-e2e";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName));
        }
    }

    private static void SetTimeout(E2ETimeoutsOptions timeouts, string timeoutName, int value)
    {
        switch (timeoutName)
        {
            case nameof(E2ETimeoutsOptions.ContainerStartupSeconds): timeouts.ContainerStartupSeconds = value; break;
            case nameof(E2ETimeoutsOptions.ApiStartupSeconds): timeouts.ApiStartupSeconds = value; break;
            case nameof(E2ETimeoutsOptions.WebStartupSeconds): timeouts.WebStartupSeconds = value; break;
            case nameof(E2ETimeoutsOptions.ProcessShutdownSeconds): timeouts.ProcessShutdownSeconds = value; break;
            case nameof(E2ETimeoutsOptions.HttpRequestSeconds): timeouts.HttpRequestSeconds = value; break;
            case nameof(E2ETimeoutsOptions.BrowserActionMilliseconds): timeouts.BrowserActionMilliseconds = value; break;
            case nameof(E2ETimeoutsOptions.ScenarioSeconds): timeouts.ScenarioSeconds = value; break;
            case nameof(E2ETimeoutsOptions.TestSessionSeconds): timeouts.TestSessionSeconds = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(timeoutName));
        }
    }

    private static string ExpectedTimeoutError(string timeoutName) => timeoutName switch
    {
        nameof(E2ETimeoutsOptions.ContainerStartupSeconds) => "Timeouts.ContainerStartupSeconds must be between 1 and 600 seconds.",
        nameof(E2ETimeoutsOptions.ApiStartupSeconds) => "Timeouts.ApiStartupSeconds must be between 1 and 600 seconds.",
        nameof(E2ETimeoutsOptions.WebStartupSeconds) => "Timeouts.WebStartupSeconds must be between 1 and 600 seconds.",
        nameof(E2ETimeoutsOptions.ProcessShutdownSeconds) => "Timeouts.ProcessShutdownSeconds must be between 1 and 120 seconds.",
        nameof(E2ETimeoutsOptions.HttpRequestSeconds) => "Timeouts.HttpRequestSeconds must be between 1 and 300 seconds.",
        nameof(E2ETimeoutsOptions.BrowserActionMilliseconds) => "Timeouts.BrowserActionMilliseconds must be between 100 and 120000 milliseconds.",
        nameof(E2ETimeoutsOptions.ScenarioSeconds) => "Timeouts.ScenarioSeconds must be between 1 and 1800 seconds.",
        nameof(E2ETimeoutsOptions.TestSessionSeconds) => "Timeouts.TestSessionSeconds must be between 1 and 3600 seconds.",
        _ => throw new ArgumentOutOfRangeException(nameof(timeoutName))
    };

    private static void AssertValidationFailure(E2EOptions options, string expectedError)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => E2EOptionsValidator.Validate(options, RepositoryRoot.Find()));

        Assert.That(exception!.Message, Is.EqualTo("Invalid E2E configuration: " + expectedError));
    }

    private static void WithEnvironmentVariables(IReadOnlyDictionary<string, string?> variables, Action assertion)
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
