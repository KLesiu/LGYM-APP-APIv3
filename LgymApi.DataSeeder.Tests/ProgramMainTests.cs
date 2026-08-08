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
        var originalMigrationConnection = Environment.GetEnvironmentVariable("LGYM_MIGRATION_POSTGRES");
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", basePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", "true");
            Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", null);

            Console.SetIn(TextReader.Null);
            Console.SetOut(new StringWriter());

            var code = await Program.Main(Array.Empty<string>());

            code.Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", originalBasePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", originalTestMode);
            Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", originalMigrationConnection);
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
        var originalMigrationConnection = Environment.GetEnvironmentVariable("LGYM_MIGRATION_POSTGRES");
        var originalIn = Console.In;
        var originalOut = Console.Out;

        try
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", basePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", "true");
            Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", "Host=maintenance;Database=seeder;Username=maintenance;Password=test-only");

            Console.SetIn(TextReader.Null);
            Console.SetOut(new StringWriter());

            var code = await Program.Main(Array.Empty<string>());

            code.Should().Be(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LGYM_SEEDER_BASE_PATH", originalBasePath);
            Environment.SetEnvironmentVariable("LGYM_SEEDER_TEST_MODE", originalTestMode);
            Environment.SetEnvironmentVariable("LGYM_MIGRATION_POSTGRES", originalMigrationConnection);
            Console.SetIn(originalIn);
            Console.SetOut(originalOut);
            Directory.Delete(basePath, recursive: true);
        }
    }

    [TestCase(new[] { "--migrate-only" }, SeederMode.MigrateOnly)]
    [TestCase(new[] { "--prepare-hangfire" }, SeederMode.PrepareHangfire)]
    [TestCase(new[] { "--seed" }, SeederMode.Seed)]
    [TestCase(new string[0], SeederMode.Seed)]
    public void TryParseMode_RecognizesSupportedOfflineModes(string[] args, SeederMode expectedMode)
    {
        Program.TryParseMode(args, out var mode).Should().BeTrue();
        mode.Should().Be(expectedMode);
    }

    [Test]
    public void TryParseMode_RejectsUnknownOrCombinedModes()
    {
        Program.TryParseMode(["--migrate-only", "--prepare-hangfire"], out _).Should().BeFalse();
        Program.TryParseMode(["--unknown"], out _).Should().BeFalse();
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
