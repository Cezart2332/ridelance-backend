using Domain.Office;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Office;

internal sealed class OfficeAppointmentConfiguration : IEntityTypeConfiguration<OfficeAppointment>
{
    public void Configure(EntityTypeBuilder<OfficeAppointment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasIndex(a => a.Date);
        // One confirmed booking per slot.
        builder.HasIndex(a => new { a.Date, a.StartTime })
            .IsUnique()
            .HasFilter("status = 'Confirmed'");

        builder.Property(a => a.FullName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Email).HasMaxLength(320).IsRequired();
        builder.Property(a => a.Phone).HasMaxLength(32).IsRequired();
        builder.Property(a => a.Reason).HasMaxLength(2000);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
    }
}

internal sealed class OfficeScheduleDayConfiguration : IEntityTypeConfiguration<OfficeScheduleDay>
{
    public void Configure(EntityTypeBuilder<OfficeScheduleDay> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.Day).IsUnique();
        builder.Property(s => s.Day).HasConversion<string>().HasMaxLength(16);
    }
}

internal sealed class OfficeBlockedSlotConfiguration : IEntityTypeConfiguration<OfficeBlockedSlot>
{
    public void Configure(EntityTypeBuilder<OfficeBlockedSlot> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => b.Date);
        builder.Property(b => b.Note).HasMaxLength(500);
    }
}
