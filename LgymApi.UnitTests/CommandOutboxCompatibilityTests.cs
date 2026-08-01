using FluentAssertions;
using LgymApi.Application.Reporting.Contracts.BackgroundCommands;
using LgymApi.Application.Platform.Contracts.BackgroundCommands;
using LgymApi.BackgroundWorker;
using LgymApi.BackgroundWorker.Runtime;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class CommandOutboxCompatibilityTests
{
    [Test]
    public async Task AcceptedProgressWriter_PersistsCanonicalLegacyIdAndGoldenPayload()
    {
        var contract = LegacyCommandContractManifest.All.Single(row =>
            row.CommandType == typeof(ReportSubmissionAcceptedProgressCommand));
        var command = (ReportSubmissionAcceptedProgressCommand)contract.Command;
        var resolver = Substitute.For<IBackgroundActionResolver>();
        resolver.GetHandlerTypeNames(typeof(ReportSubmissionAcceptedProgressCommand))
            .Returns(contract.HandlerTypeFullNames);
        var runtime = Substitute.For<ICommandEnvelopeRuntime>();
        CommandEnvelopeRequest? capturedRequest = null;
        runtime.StageAsync(Arg.Do<CommandEnvelopeRequest>(request => capturedRequest = request), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new CommandEnvelopeReceipt("envelope-id", false)));
        var writer = new CommandOutboxWriter(
            resolver,
            CommandContractRegistry.CreateDefault(),
            runtime,
            NullLogger<CommandOutboxWriter>.Instance);

        var result = await writer.StageAsync(command);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.CommandId.Should().Be(contract.CanonicalId)
            .And.NotBe(contract.FutureClrNameReadAlias);
        capturedRequest.PayloadJson.Should().Be(contract.PayloadJson);
        $"{capturedRequest.CommandId}|{capturedRequest.PayloadJson}"
            .Should().Be(contract.CorrelationInput);
        result.EnvelopeId.Should().Be("envelope-id");
        result.WasExisting.Should().BeFalse();
    }
}
