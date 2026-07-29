using Microsoft.EntityFrameworkCore;

namespace LgymApi.Platform.Persistence;

internal static class PlatformModelConfigurationRegistrar
{
    internal static void ApplyReferenceData(ModelBuilder modelBuilder, Action<ModelBuilder> applyConfigurations)
        => applyConfigurations(modelBuilder);

    internal static void ApplyReliability(ModelBuilder modelBuilder, Action<ModelBuilder> applyConfigurations)
        => applyConfigurations(modelBuilder);
}
