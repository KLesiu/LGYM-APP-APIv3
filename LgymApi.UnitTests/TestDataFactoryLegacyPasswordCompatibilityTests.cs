using FluentAssertions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Infrastructure.Data;
using LgymApi.Infrastructure.Services;
using LgymApi.TestUtils;
using Microsoft.EntityFrameworkCore;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class TestDataFactoryLegacyPasswordCompatibilityTests
{
    [Test]
    public async Task SeedUserAsync_Creates_LegacyPasswordData_Compatible_With_The_LegacyVerifier()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Id<User>.New().ToString())
            .Options;
        await using var context = new AppDbContext(options);
        const string password = "legacy-compatible-password";

        var user = await TestDataFactory.SeedUserAsync(context, password: password);
        var verifier = new LegacyPasswordService();

        Assert.Multiple(() =>
        {
            user.LegacyIterations.Should().Be(25000);
            user.LegacyKeyLength.Should().Be(512);
            user.LegacyDigest.Should().Be("sha256");
            user.LegacyHash.Should().MatchRegex("^[0-9a-f]+$");
            user.LegacySalt.Should().MatchRegex("^[0-9a-f]+$");
            verifier.Verify(password, user.LegacyHash!, user.LegacySalt!, user.LegacyIterations, user.LegacyKeyLength, user.LegacyDigest).Should().BeTrue();
            verifier.Verify("wrong-password", user.LegacyHash!, user.LegacySalt!, user.LegacyIterations, user.LegacyKeyLength, user.LegacyDigest).Should().BeFalse();
        });
    }
}
