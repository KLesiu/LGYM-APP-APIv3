using LgymApi.Domain.ValueObjects;

namespace LgymApi.DataSeeder.Tests;

[TestFixture]
public sealed class ProgramMainTests
{
    [Test]
    public async Task Main_Should_Return_NonZero_When_ConnectionString_Is_Missing()
    {
        var basePath = CreateTempRepo(withBaseSettings: true, includeConnection: false);
        var originalBasePath = Environment.GetEnvironmentVariable("LGYM_SEEDER_BASE_PATH");
        var originalTestMode = Environment.GetEnvironmentVariable("LGYM_SEEDER_TEST_MODE");
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", basePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", "true");

            Console.SetIn(TextReader.Null);
            var output = new StringWriter();
            Console.SetOut(output);

            var code = await Program.Main(Array.Empty<string>());

            code.Should().Be(1);
            output.ToString().Should().Contain("Connection: <empty>");
            output.ToString().Should().NotContain("Password=");
        }
        finally
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", originalBasePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", originalTestMode);
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Directory.Delete(basePath, recursive: true);
        }
    }

    [Test]
    public async Task Main_Should_Return_Zero_In_Test_Mode_When_Config_Is_Valid()
    {
        var basePath = CreateTempRepo(withBaseSettings: true);
        var originalBasePath = Environment.GetEnvironmentVariable("LGYM_SEEDER_BASE_PATH");
        var originalTestMode = Environment.GetEnvironmentVariable("LGYM_SEEDER_TEST_MODE");
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", basePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", "true");

            Console.SetIn(TextReader.Null);
            Console.SetOut(new StringWriter());

            var code = await Program.Main(Array.Empty<string>());

            code.Should().Be(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", originalBasePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", originalTestMode);
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Directory.Delete(basePath, recursive: true);
        }
    }

    private static string CreateTempRepo(bool withBaseSettings, bool includeConnection = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "lgym-seeder-program", Id<ProgramMainTests>.New().ToString());
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "LgymApi.sln"), string.Empty);

        var apiRoot = Path.Combine(root, "LgymApi.Api");
        Directory.CreateDirectory(apiRoot);

        if (withBaseSettings)
        {
            var baseSettings = includeConnection
                ? "{" + "\"ConnectionStrings\": { \"Postgres\": \"Host=localhost\" }" + "}"
                : "{" + "\"ConnectionStrings\": {}" + "}";
            File.WriteAllText(Path.Combine(apiRoot, "appsettings.json"), baseSettings);
        }

        return root;
    }
}
