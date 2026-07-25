using FluentAssertions;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class ApiAuthenticationRegistrationExtractionTests
{
    [Test]
    public void Program_DelegatesAuthenticationRegistrationToApiOnlyExtension()
    {
        var repositoryRoot = FindRepositoryRoot();
        var programSource = File.ReadAllText(Path.Combine(repositoryRoot, "LgymApi.Api", "Program.cs"));
        var extensionPath = Path.Combine(repositoryRoot, "LgymApi.Api", "Configuration", "ApiAuthenticationExtensions.cs");

        File.Exists(extensionPath).Should().BeTrue();
        programSource.Should().Contain("builder.Services.AddApiAuthentication(builder.Configuration);");
        programSource.Should().NotContain("AddAuthentication(JwtBearerDefaults.AuthenticationScheme)");
        programSource.Should().NotContain("AddJwtBearer(");
        programSource.Should().NotContain("new TokenValidationParameters");
        programSource.Should().NotContain("new JwtBearerEvents");

        var extensionSource = File.ReadAllText(extensionPath);
        extensionSource.Should().Contain("namespace LgymApi.Api.Configuration;");
        extensionSource.Should().Contain("AddAuthentication(JwtBearerDefaults.AuthenticationScheme)");
        extensionSource.Should().Contain("AddJwtBearer(");
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LgymApi.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root containing LgymApi.sln.");
    }
}
