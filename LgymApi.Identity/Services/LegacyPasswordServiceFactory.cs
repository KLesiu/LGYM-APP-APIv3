namespace LgymApi.Application.Services;

internal static class LegacyPasswordServiceFactory
{
    public static ILegacyPasswordService Create() => new LgymApi.Infrastructure.Services.LegacyPasswordService();
}
