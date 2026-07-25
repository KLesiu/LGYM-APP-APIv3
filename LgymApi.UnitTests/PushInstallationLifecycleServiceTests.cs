using FluentAssertions;
using LgymApi.Application.BuildingBlocks.Errors;
using LgymApi.Application.Identity.Errors;
using LgymApi.Application.Notifications;
using LgymApi.Application.Notifications.Models;
using LgymApi.Application.Notifications.Repositories;
using LgymApi.Application.Repositories;
using LgymApi.Domain.Entities;
using LgymApi.Domain.ValueObjects;
using LgymApi.Resources;
using NSubstitute;

namespace LgymApi.UnitTests;

[TestFixture]
public sealed class PushInstallationLifecycleServiceTests
{
    private IPushInstallationRepository _pushInstallationRepository = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PushInstallationLifecycleService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _pushInstallationRepository = Substitute.For<IPushInstallationRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _service = new PushInstallationLifecycleService(_pushInstallationRepository, _unitOfWork);
    }

    [Test]
    public async Task RegisterAsync_NormalizesValues_StagesRegistrationAndCommitsOnce()
    {
        var userId = CreateUserId();
        var sessionId = Id<UserSession>.New();
        var beforeRegistration = DateTimeOffset.UtcNow;

        var result = await _service.RegisterAsync(
            userId,
            sessionId,
            new RegisterPushInstallationInput(" device-1 ", " ios ", " token-1 ", " 2.0.0 ", " production ", " authorized "));
        var afterRegistration = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        await _pushInstallationRepository.Received(1).UpsertForUserSessionAsync(
            Arg.Is<PushInstallationRegistration>(registration =>
                registration.InstallationId == "device-1"
                && registration.Platform == "ios"
                && registration.FcmToken == "token-1"
                && registration.AppVersion == "2.0.0"
                && registration.Environment == "production"
                && registration.PermissionStatus == "authorized"
                && registration.UserId == userId
                && registration.SessionId == sessionId
                && registration.LastSeenAt.Offset == TimeSpan.Zero
                && registration.LastSeenAt >= beforeRegistration
                && registration.LastSeenAt <= afterRegistration),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase(" \t ", " \t ")]
    public async Task RegisterAsync_WhenOptionalValuesAreBlank_StagesNullOptionalValues(
        string? appVersion,
        string? permissionStatus)
    {
        var result = await _service.RegisterAsync(
            CreateUserId(),
            Id<UserSession>.New(),
            new RegisterPushInstallationInput("device-1", "android", "token-1", appVersion, "development", permissionStatus));

        result.IsSuccess.Should().BeTrue();
        await _pushInstallationRepository.Received(1).UpsertForUserSessionAsync(
            Arg.Is<PushInstallationRegistration>(registration =>
                registration.AppVersion == null
                && registration.PermissionStatus == null),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterAsync_WhenUnauthenticated_DoesNotWriteOrCommit()
    {
        var result = await _service.RegisterAsync(
            null,
            Id<UserSession>.New(),
            new RegisterPushInstallationInput("device-1", "android", "token-1", null, "development", null));

        result.Error.Should().BeOfType<UserUnauthorizedError>();
        result.Error.Message.Should().Be(Messages.Unauthorized);
        await _pushInstallationRepository.DidNotReceive().UpsertForUserSessionAsync(
            Arg.Any<PushInstallationRegistration>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RegisterAsync_WhenSessionIsMissing_DoesNotWriteOrCommit()
    {
        var result = await _service.RegisterAsync(
            CreateUserId(),
            null,
            new RegisterPushInstallationInput("device-1", "android", "token-1", null, "development", null));

        result.Error.Should().BeOfType<UserUnauthorizedError>();
        result.Error.Message.Should().Be(Messages.Unauthorized);
        await _pushInstallationRepository.DidNotReceive().UpsertForUserSessionAsync(
            Arg.Any<PushInstallationRegistration>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [TestCase("", "android", "token-1", "development")]
    [TestCase("device-1", "", "token-1", "development")]
    [TestCase("device-1", "android", "", "development")]
    [TestCase("device-1", "android", "token-1", "")]
    [TestCase(null, "android", "token-1", "development")]
    [TestCase("device-1", null, "token-1", "development")]
    [TestCase("device-1", "android", null, "development")]
    [TestCase("device-1", "android", "token-1", null)]
    [TestCase(" ", "android", "token-1", "development")]
    [TestCase("device-1", " ", "token-1", "development")]
    [TestCase("device-1", "android", " ", "development")]
    [TestCase("device-1", "android", "token-1", " ")]
    public async Task RegisterAsync_WhenRequiredValueIsBlank_DoesNotWriteOrCommit(
        string? installationKey,
        string? platform,
        string? fcmToken,
        string? environment)
    {
        var result = await _service.RegisterAsync(
            CreateUserId(),
            Id<UserSession>.New(),
            new RegisterPushInstallationInput(installationKey!, platform!, fcmToken!, null, environment!, null));

        result.Error.Should().BeOfType<InvalidUserError>();
        result.Error.Message.Should().Be(Messages.FieldRequired);
        await _pushInstallationRepository.DidNotReceive().UpsertForUserSessionAsync(
            Arg.Any<PushInstallationRegistration>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UnregisterAsync_StagesUnregisteredActionAndCommitsOnce()
    {
        var userId = CreateUserId();
        var sessionId = Id<UserSession>.New();
        var beforeUnregister = DateTimeOffset.UtcNow;

        var result = await _service.UnregisterAsync(userId, sessionId, new PushInstallationActionInput(" device-1 "));
        var afterUnregister = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        await _pushInstallationRepository.Received(1).DisableBoundForUserOrSessionAsync(
            "device-1",
            userId,
            sessionId,
            Arg.Is<DateTimeOffset>(disabledAt =>
                disabledAt.Offset == TimeSpan.Zero
                && disabledAt >= beforeUnregister
                && disabledAt <= afterUnregister),
            "Unregistered",
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DisassociateAsync_StagesActionAndCommitsOnce()
    {
        var userId = CreateUserId();
        var sessionId = Id<UserSession>.New();
        var beforeDisassociate = DateTimeOffset.UtcNow;

        var result = await _service.DisassociateAsync(userId, sessionId, new PushInstallationActionInput(" device-1 "));
        var afterDisassociate = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeTrue();
        await _pushInstallationRepository.Received(1).DisassociateBoundForUserOrSessionAsync(
            "device-1",
            userId,
            sessionId,
            Arg.Is<DateTimeOffset>(lastSeenAt =>
                lastSeenAt.Offset == TimeSpan.Zero
                && lastSeenAt >= beforeDisassociate
                && lastSeenAt <= afterDisassociate),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task PushInstallationActions_WhenInstallationKeyIsBlank_DoNotWriteOrCommit(string? installationKey)
    {
        var userId = CreateUserId();
        var sessionId = Id<UserSession>.New();

        var unregisterResult = await _service.UnregisterAsync(userId, sessionId, new PushInstallationActionInput(installationKey!));
        var disassociateResult = await _service.DisassociateAsync(userId, sessionId, new PushInstallationActionInput(installationKey!));

        unregisterResult.Error.Should().BeOfType<InvalidUserError>();
        unregisterResult.Error.Message.Should().Be(Messages.FieldRequired);
        disassociateResult.Error.Should().BeOfType<InvalidUserError>();
        disassociateResult.Error.Message.Should().Be(Messages.FieldRequired);
        await _pushInstallationRepository.DidNotReceive().DisableBoundForUserOrSessionAsync(
            Arg.Any<string>(),
            Arg.Any<Id<User>>(),
            Arg.Any<Id<UserSession>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _pushInstallationRepository.DidNotReceive().DisassociateBoundForUserOrSessionAsync(
            Arg.Any<string>(),
            Arg.Any<Id<User>>(),
            Arg.Any<Id<UserSession>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PushInstallationActions_WhenSessionIsMissing_DoNotWriteOrCommit()
    {
        var userId = CreateUserId();

        var unregisterResult = await _service.UnregisterAsync(userId, null, new PushInstallationActionInput("device-1"));
        var disassociateResult = await _service.DisassociateAsync(userId, null, new PushInstallationActionInput("device-1"));

        unregisterResult.Error.Should().BeOfType<UserUnauthorizedError>();
        unregisterResult.Error.Message.Should().Be(Messages.Unauthorized);
        disassociateResult.Error.Should().BeOfType<UserUnauthorizedError>();
        disassociateResult.Error.Message.Should().Be(Messages.Unauthorized);
        await _pushInstallationRepository.DidNotReceive().DisableBoundForUserOrSessionAsync(
            Arg.Any<string>(),
            Arg.Any<Id<User>>(),
            Arg.Any<Id<UserSession>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _pushInstallationRepository.DidNotReceive().DisassociateBoundForUserOrSessionAsync(
            Arg.Any<string>(),
            Arg.Any<Id<User>>(),
            Arg.Any<Id<UserSession>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PushInstallationActions_WhenUserIsMissing_DoNotWriteOrCommit()
    {
        var sessionId = Id<UserSession>.New();

        var unregisterResult = await _service.UnregisterAsync(null, sessionId, new PushInstallationActionInput("device-1"));
        var disassociateResult = await _service.DisassociateAsync(null, sessionId, new PushInstallationActionInput("device-1"));

        unregisterResult.Error.Should().BeOfType<UserUnauthorizedError>();
        unregisterResult.Error.Message.Should().Be(Messages.Unauthorized);
        disassociateResult.Error.Should().BeOfType<UserUnauthorizedError>();
        disassociateResult.Error.Message.Should().Be(Messages.Unauthorized);
        await _pushInstallationRepository.DidNotReceive().DisableBoundForUserOrSessionAsync(
            Arg.Any<string>(),
            Arg.Any<Id<User>>(),
            Arg.Any<Id<UserSession>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _pushInstallationRepository.DidNotReceive().DisassociateBoundForUserOrSessionAsync(
            Arg.Any<string>(),
            Arg.Any<Id<User>>(),
            Arg.Any<Id<UserSession>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PushInstallationActions_WhenNoBoundInstallationExists_ReturnSuccessAndCommitOnce()
    {
        var userId = CreateUserId();
        var sessionId = Id<UserSession>.New();
        _pushInstallationRepository.DisableBoundForUserOrSessionAsync(
                Arg.Any<string>(),
                Arg.Any<Id<User>>(),
                Arg.Any<Id<UserSession>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));
        _pushInstallationRepository.DisassociateBoundForUserOrSessionAsync(
                Arg.Any<string>(),
                Arg.Any<Id<User>>(),
                Arg.Any<Id<UserSession>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var unregisterResult = await _service.UnregisterAsync(userId, sessionId, new PushInstallationActionInput("device-1"));
        var disassociateResult = await _service.DisassociateAsync(userId, sessionId, new PushInstallationActionInput("device-1"));

        unregisterResult.IsSuccess.Should().BeTrue();
        disassociateResult.IsSuccess.Should().BeTrue();
        await _pushInstallationRepository.Received(1).DisableBoundForUserOrSessionAsync(
            "device-1",
            userId,
            sessionId,
            Arg.Any<DateTimeOffset>(),
            "Unregistered",
            Arg.Any<CancellationToken>());
        await _pushInstallationRepository.Received(1).DisassociateBoundForUserOrSessionAsync(
            "device-1",
            userId,
            sessionId,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StageDisassociateForSessionAsync_StagesWithoutCommitting()
    {
        var sessionId = Id<UserSession>.New();
        var beforeDisassociate = DateTimeOffset.UtcNow;

        await _service.StageDisassociateForSessionAsync(sessionId);
        var afterDisassociate = DateTimeOffset.UtcNow;

        await _pushInstallationRepository.Received(1).DisassociateForSessionAsync(
            sessionId,
            Arg.Is<DateTimeOffset>(lastSeenAt =>
                lastSeenAt.Offset == TimeSpan.Zero
                && lastSeenAt >= beforeDisassociate
                && lastSeenAt <= afterDisassociate),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Id<User> CreateUserId()
    {
        return Id<User>.New();
    }
}
