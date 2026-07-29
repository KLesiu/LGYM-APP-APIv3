using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Identity.Persistence.Configurations;

internal sealed class UserTutorialStepProgressEntityTypeConfiguration : IEntityTypeConfiguration<UserTutorialStepProgress>
{
    public void Configure(EntityTypeBuilder<UserTutorialStepProgress> builder)
    {
        builder.ToTable("UserTutorialStepProgresses");

        builder.Property(utsp => utsp.TutorialStep).HasConversion<string>();

        builder.HasOne(utsp => utsp.UserTutorialProgress)
            .WithMany(utp => utp.CompletedSteps)
            .HasForeignKey(utsp => utsp.UserTutorialProgressId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(utsp => new { utsp.UserTutorialProgressId, utsp.TutorialStep })
            .IsUnique()
            .HasFilter(IdentityConfigurationFilters.ActiveRowsFilter);
    }
}
