using Domain.AppSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.AppSettings;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.Key).IsUnique();

        builder.Property(s => s.Key).HasMaxLength(128).IsRequired();
        builder.Property(s => s.ValueJson).HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.Description).HasMaxLength(512);
    }
}
