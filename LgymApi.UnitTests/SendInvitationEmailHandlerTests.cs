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
public sealed class SendInvitationEmailHandlerTests
{
    private IInvitationCreatedEmailPreparationPort _preparationPort = null!;
    private IInvitationCreatedEmailDeliveryPort _deliveryPort = null!;
    private SendInvitationEmailHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _preparationPort = Substitute.For<IInvitationCreatedEmailPreparationPort>();
        _deliveryPort = Substitute.For<IInvitationCreatedEmailDeliveryPort>();
        _handler = new SendInvitationEmailHandler(_preparationPort, _deliveryPort);
    }

    [Test]
    public async Task ExecuteAsync_PreparesRawCommandAndDeliversScalarFacts()
    {
        var invitationId = Id<TrainerInvitation>.New();
        var preparation = new InvitationCreatedEmailPreparation(
            invitationId.ToString(),
            Id<User>.New().ToString(),
            Id<User>.New().ToString(),
            "invitee@example.com",
            "CODE123",
            new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero),
            "Coach",
            "coach@example.com",
            "pl-PL",
            "Europe/Warsaw",
            "Trainee",
            "trainee@example.com",
            "Europe/Madrid");
        _preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InvitationCreatedEmailPreparation?>(preparation));
        using var cancellation = new CancellationTokenSource();

        await _handler.ExecuteAsync(new InvitationCreatedCommand { InvitationId = invitationId }, cancellation.Token);

        await _preparationPort.Received(1).PrepareAsync(
            Arg.Is<string>(payload => JsonSerializer.Deserialize<InvitationCreatedCommand>(payload, SharedSerializationOptions.Current)!.InvitationId == invitationId),
            cancellation.Token);
        await _deliveryPort.Received(1).DeliverAsync(
            new InvitationCreatedEmailDeliveryRequest(
                preparation.InvitationId,
                preparation.TrainerId,
                preparation.TraineeId,
                preparation.InviteeEmail,
                preparation.InvitationCode,
                preparation.ExpiresAt,
                preparation.TrainerName,
                preparation.TrainerEmail,
                preparation.TrainerCultureName,
                preparation.TrainerTimeZone,
                preparation.TraineeName,
                preparation.TraineeEmail,
                preparation.TraineeTimeZone),
            cancellation.Token);
    }

    [Test]
    public async Task ExecuteAsync_WhenPreparationReturnsNull_DoesNotDeliver()
    {
        _preparationPort.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<InvitationCreatedEmailPreparation?>(null));

        await _handler.ExecuteAsync(new InvitationCreatedCommand { InvitationId = Id<TrainerInvitation>.New() });

        await _deliveryPort.DidNotReceive().DeliverAsync(
            Arg.Any<InvitationCreatedEmailDeliveryRequest>(),
            Arg.Any<CancellationToken>());
    }
}
