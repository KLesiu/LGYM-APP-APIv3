using System.Text.Json;
using LgymApi.Application.Coaching.Contracts.BackgroundCommands;
using LgymApi.Application.Coaching.Contracts.Notifications;
using LgymApi.Application.Notifications.Contracts.Email;
using LgymApi.Application.Platform.Contracts.Serialization;
using LgymApi.BackgroundWorker.Actions;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using NSubstitute;
using NUnit.Framework;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class InvitationRevokedEmailHandlerTests
{
    private IInvitationRevokedEmailPreparationPort _preparationPort = null!;
    private IInvitationRevokedEmailDeliveryPort _deliveryPort = null!;
    private InvitationRevokedEmailHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _preparationPort = Substitute.For<IInvitationRevokedEmailPreparationPort>();
        _deliveryPort = Substitute.For<IInvitationRevokedEmailDeliveryPort>();
        _handler = new InvitationRevokedEmailHandler(_preparationPort, _deliveryPort);
    }

    [Test]
    public async Task ExecuteAsync_PreparesRawCommandAndDeliversScalarFacts()
    {
        var invitationId = Id<TrainerInvitation>.New();
        var preparation = new InvitationRevokedEmailPreparation(
            invitationId.ToString(),
            Id<User>.New().ToString(),
            "invitee@example.com",
            "coach@example.com",
            "pl-PL",
            "Europe/Warsaw",
            "Coach");
        _preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InvitationRevokedEmailPreparation?>(preparation));
        using var cancellation = new CancellationTokenSource();

        await _handler.ExecuteAsync(new InvitationRevokedCommand { InvitationId = invitationId }, cancellation.Token);

        await _preparationPort.Received(1).PrepareAsync(
            Arg.Is<string>(payload => JsonSerializer.Deserialize<InvitationRevokedCommand>(payload, SharedSerializationOptions.Current)!.InvitationId == invitationId),
            cancellation.Token);
        await _deliveryPort.Received(1).DeliverAsync(
            new InvitationRevokedEmailDeliveryRequest(
                preparation.InvitationId,
                preparation.TrainerId,
                preparation.InviteeEmail,
                preparation.TrainerEmail,
                preparation.TrainerCultureName,
                preparation.TrainerTimeZone,
                preparation.TrainerName),
            cancellation.Token);
    }

    [Test]
    public async Task ExecuteAsync_WhenPreparationReturnsNull_DoesNotDeliver()
    {
        _preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InvitationRevokedEmailPreparation?>(null));

        await _handler.ExecuteAsync(new InvitationRevokedCommand { InvitationId = Id<TrainerInvitation>.New() });

        await _deliveryPort.DidNotReceive().DeliverAsync(
            Arg.Any<InvitationRevokedEmailDeliveryRequest>(),
            Arg.Any<CancellationToken>());
    }
}
