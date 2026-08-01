using LgymApi.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LgymApi.Infrastructure.Data.Configurations.Nutrition;

internal sealed class DietMealEntityTypeConfiguration : IEntityTypeConfiguration<DietMeal>
{
    public void Configure(EntityTypeBuilder<DietMeal> builder)
    {
        builder.ToTable("DietMeals");

        builder.Property(e => e.Name).HasMaxLength(160).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);

        builder.HasIndex(e => new { e.DietPlanId, e.Order });

        builder.HasOne(e => e.DietPlan)
            .WithMany(e => e.Meals)
            .HasForeignKey(e => e.DietPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
