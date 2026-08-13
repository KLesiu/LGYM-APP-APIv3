namespace LgymApi.E2ETests.Harness;

[TestFixture]
[Category("WebHarness")]
public sealed class NodeNpmToolResolverTests
{
    [Test]
    public async Task NodeNpm_resolves_absolute_node_and_matching_npm_cli_before_installation()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();

        // When
        var tools = fixture.CreateToolResolver().Resolve();

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(tools.NodeExecutable, Is.EqualTo(Path.GetFullPath(fixture.NodeExecutable)));
            Assert.That(tools.NpmCliScript, Is.EqualTo(Path.GetFullPath(fixture.NpmCliScript)));
            Assert.That(tools.ToString(), Is.EqualTo("<node-npm-tools>"));
        });
    }

    [Test]
    public async Task NodeNpm_rejects_missing_matching_npm_cli_without_running_a_command()
    {
        // Given
        await using var fixture = await Task3WebSourceRunFixture.CreateAsync();
        File.Delete(fixture.NpmCliScript);
        var runner = new Task3NodeNpmCommandRunner();

        // When
        var exception = Assert.Throws<InvalidOperationException>(() => fixture.CreateToolResolver().Resolve());

        // Then
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo(NodeNpmToolResolver.PrerequisiteMessage));
            Assert.That(runner.Requests, Is.Empty);
        });
    }
}
