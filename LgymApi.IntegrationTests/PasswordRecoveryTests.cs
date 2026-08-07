using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FluentAssertions;
using LgymApi.BackgroundWorker.Common.Notifications;
using LgymApi.Domain.Entities;
using LgymApi.Domain.Enums;
using LgymApi.Domain.Notifications;
using LgymApi.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LgymApi.IntegrationTests;

[TestFixture]
public sealed class PasswordRecoveryTests : IntegrationTestBase
{
    private const string ForgotPasswordGenericMessage = "If an account with that email exists, a password reset link has been sent.";
    private const string ResetPasswordSuccessMessage = "Password reset successfully.";
    private const string ResetPasswordInvalidMessage = "Invalid or expired reset token.";

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/forgot-password", "public", "anonymous-intended-behavior")]
    public async Task PasswordRecovery_ForgotPassword_WithExistingEmail_ReturnsOkGenericMessageAndCreatesNotificationRow()
    {
        var user = await SeedUserAsync(
            name: "recover-existing",
            email: "recover-existing@example.com",
            password: "old-password");

        var response = await Client.PostAsJsonAsync("/api/forgot-password", new
        {
            email = "recover-existing@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ForgotPasswordGenericMessage);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var token = await db.PasswordResetTokens.SingleOrDefaultAsync(t => t.UserId == user.Id);
        token.Should().NotBeNull();
        token!.IsUsed.Should().BeFalse();
        token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(25));

        var notification = await db.NotificationMessages.SingleOrDefaultAsync(n =>
            n.Type == EmailNotificationTypes.PasswordRecovery
            && n.Recipient == "recover-existing@example.com");
        notification.Should().NotBeNull();
    }

    [Test]
    public async Task PasswordRecovery_ForgotPassword_WithNonExistingEmail_ReturnsOkWithSameGenericMessage()
    {
        var response = await Client.PostAsJsonAsync("/api/forgot-password", new
        {
            email = "missing-user@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ForgotPasswordGenericMessage);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tokenCount = await db.PasswordResetTokens.CountAsync();
        tokenCount.Should().Be(0);

        var notificationCount = await db.NotificationMessages
            .CountAsync(n => n.Type == EmailNotificationTypes.PasswordRecovery);
        notificationCount.Should().Be(0);
    }

    [Test]
    public async Task PasswordRecovery_ForgotPassword_WithDeletedUser_ReturnsOkAndDoesNotCreateToken()
    {
        var user = await SeedUserAsync(
            name: "recover-deleted",
            email: "recover-deleted@example.com",
            password: "old-password",
            isDeleted: true);

        var response = await Client.PostAsJsonAsync("/api/forgot-password", new
        {
            email = "recover-deleted@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ForgotPasswordGenericMessage);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tokenCount = await db.PasswordResetTokens.CountAsync(t => t.UserId == user.Id);
        tokenCount.Should().Be(0);
    }

    [Test]
    [LgymApi.IntegrationTests.Authorization.AuthorizationEvidence("POST", "/api/reset-password", "public", "anonymous-intended-behavior")]
    public async Task PasswordRecovery_ResetPassword_WithValidToken_ReturnsOkAndAllowsLoginWithNewPassword()
    {
        var user = await SeedUserAsync(
            name: "reset-valid",
            email: "reset-valid@example.com",
            password: "old-password");
        const string validToken = "VALID-RESET-TOKEN-001";
        await SeedPasswordResetTokenAsync(user.Id, validToken, DateTimeOffset.UtcNow.AddMinutes(30), isUsed: false);

        var response = await Client.PostAsJsonAsync("/api/reset-password", new
        {
            token = validToken,
            newPassword = "new-password-123",
            confirmPassword = "new-password-123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ResetPasswordSuccessMessage);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenEntity = await db.PasswordResetTokens.SingleAsync(t => t.UserId == user.Id);
            tokenEntity.IsUsed.Should().BeTrue();
        }

        var oldPasswordLoginResponse = await Client.PostAsJsonAsync("/api/login", new
        {
            name = "reset-valid",
            password = "old-password"
        });
        oldPasswordLoginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newPasswordLoginResponse = await Client.PostAsJsonAsync("/api/login", new
        {
            name = "reset-valid",
            password = "new-password-123"
        });
        newPasswordLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await newPasswordLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();
        loginBody!.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task PasswordRecovery_ResetPassword_WithExpiredToken_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(
            name: "reset-expired",
            email: "reset-expired@example.com",
            password: "old-password");
        const string expiredToken = "EXPIRED-RESET-TOKEN-001";
        await SeedPasswordResetTokenAsync(user.Id, expiredToken, DateTimeOffset.UtcNow.AddMinutes(-1), isUsed: false);

        var response = await Client.PostAsJsonAsync("/api/reset-password", new
        {
            token = expiredToken,
            newPassword = "new-password-123",
            confirmPassword = "new-password-123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ResetPasswordInvalidMessage);
    }

    [Test]
    public async Task PasswordRecovery_ResetPassword_WithUsedToken_ReturnsBadRequest()
    {
        var user = await SeedUserAsync(
            name: "reset-used",
            email: "reset-used@example.com",
            password: "old-password");
        const string usedToken = "USED-RESET-TOKEN-001";
        await SeedPasswordResetTokenAsync(user.Id, usedToken, DateTimeOffset.UtcNow.AddMinutes(30), isUsed: true);

        var response = await Client.PostAsJsonAsync("/api/reset-password", new
        {
            token = usedToken,
            newPassword = "new-password-123",
            confirmPassword = "new-password-123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ResetPasswordInvalidMessage);
    }

    [Test]
    public async Task PasswordRecovery_ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync("/api/reset-password", new
        {
            token = "NOT-A-REAL-TOKEN",
            newPassword = "new-password-123",
            confirmPassword = "new-password-123"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ResetPasswordInvalidMessage);
    }

    [Test]
    public async Task PasswordRecovery_ResetPassword_OnSuccess_InvalidatesPreviousSession()
    {
        var user = await SeedUserAsync(
            name: "reset-session",
            email: "reset-session@example.com",
            password: "session-old-password");

        var loginResponse = await Client.PostAsJsonAsync("/api/login", new
        {
            name = "reset-session",
            password = "session-old-password"
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();
        loginBody!.Token.Should().NotBeNullOrWhiteSpace();

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Token);
        var beforeResetCheckToken = await Client.GetAsync("/api/checkToken");
        beforeResetCheckToken.StatusCode.Should().Be(HttpStatusCode.OK);

        const string validToken = "SESSION-RESET-TOKEN-001";
        await SeedPasswordResetTokenAsync(user.Id, validToken, DateTimeOffset.UtcNow.AddMinutes(30), isUsed: false);

        var resetResponse = await Client.PostAsJsonAsync("/api/reset-password", new
        {
            token = validToken,
            newPassword = "session-new-password",
            confirmPassword = "session-new-password"
        });
        resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.Token);
        var afterResetCheckToken = await Client.GetAsync("/api/checkToken");
        afterResetCheckToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var middlewareError = await afterResetCheckToken.Content.ReadFromJsonAsync<MiddlewareErrorResponse>();
        middlewareError.Should().NotBeNull();
        middlewareError!.Message.Should().Be("Unauthorized");
    }

    [Test]
    public async Task PasswordRecovery_ForgotPassword_DispatchesEmail()
    {
        await SeedUserAsync(
            name: "recover-email",
            email: "recover-email@example.com",
            password: "old-password");

        var response = await Client.PostAsJsonAsync("/api/forgot-password", new
        {
            email = "recover-email@example.com"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<MessageResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be(ForgotPasswordGenericMessage);

        await ProcessPendingCommandsAsync();

        LgymApi.Domain.ValueObjects.Id<NotificationMessage> notificationId;
        using (var notificationScope = Factory.Services.CreateScope())
        {
            var notificationDb = notificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queuedNotification = await notificationDb.NotificationMessages.SingleAsync(n =>
                n.Type == EmailNotificationTypes.PasswordRecovery
                && n.Recipient == "recover-email@example.com");
            notificationId = queuedNotification.Id;
        }

        using (var handlerScope = Factory.Services.CreateScope())
        {
            var emailHandler = handlerScope.ServiceProvider.GetRequiredService<IEmailJobHandler>();
            await emailHandler.ProcessAsync(notificationId.ToString());
        }

        Factory.EmailSender.SentMessages.Should().ContainSingle();
        var sentMessage = Factory.EmailSender.SentMessages.Single();
        sentMessage.To.Should().Be("recover-email@example.com");
        sentMessage.Subject.Should().NotBeNullOrWhiteSpace();
        sentMessage.Body.Should().NotBeNullOrWhiteSpace();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notification = await db.NotificationMessages.SingleOrDefaultAsync(n =>
            n.Type == EmailNotificationTypes.PasswordRecovery
            && n.Recipient == "recover-email@example.com");

        notification.Should().NotBeNull();
        notification!.Status.Should().Be(EmailNotificationStatus.Sent);
        notification.SentAt.Should().NotBeNull();
    }

    private async Task SeedPasswordResetTokenAsync(
        LgymApi.Domain.ValueObjects.Id<User> userId,
        string plainTextToken,
        DateTimeOffset expiresAt,
        bool isUsed)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = LgymApi.Domain.ValueObjects.Id<PasswordResetToken>.New(),
            UserId = userId,
            TokenHash = ComputeSha256Hex(plainTextToken),
            ExpiresAt = expiresAt,
            IsUsed = isUsed
        });

        await db.SaveChangesAsync();
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class MiddlewareErrorResponse
    {
        [JsonPropertyName("msg")]
        public string Message { get; set; } = string.Empty;
    }

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }
}
