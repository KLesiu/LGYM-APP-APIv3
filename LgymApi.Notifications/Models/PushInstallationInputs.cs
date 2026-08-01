namespace LgymApi.Application.Notifications.Models;

public sealed record RegisterPushInstallationInput(
    string InstallationKey,
    string Platform,
    string FcmToken,
    string? AppVersion,
    string Environment,
    string? PermissionStatus);

public sealed record PushInstallationActionInput(string InstallationKey);
