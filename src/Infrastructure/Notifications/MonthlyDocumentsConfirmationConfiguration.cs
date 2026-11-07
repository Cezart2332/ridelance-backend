using Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Notifications;

internal sealed class MonthlyDocumentsConfirmationConfiguration : IEntityTypeConfiguration<MonthlyDocumentsConfirmation>
{
    public void Configure(EntityTypeBuilder<MonthlyDocumentsConfirmation> builder)
    {
        builder.HasKey(c => c.Id);

        // O confirmare pe utilizator și lună: un al doilea click nu dublează nimic.
        builder.HasIndex(c => new { c.UserId, c.Year, c.Month }).IsUnique();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
