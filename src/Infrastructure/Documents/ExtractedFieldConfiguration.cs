using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Documents;

internal sealed class ExtractedFieldConfiguration : IEntityTypeConfiguration<ExtractedField>
{
    public void Configure(EntityTypeBuilder<ExtractedField> builder)
    {
        builder.HasKey(f => f.Id);

        builder.HasIndex(f => new { f.DocumentId, f.FieldKey }).IsUnique();

        builder.Property(f => f.FieldKey).HasMaxLength(64).IsRequired();
        builder.Property(f => f.AiValue).HasMaxLength(1024);
        builder.Property(f => f.AiNormalizedValue).HasMaxLength(1024);
        builder.Property(f => f.ConfirmedValue).HasMaxLength(1024);
        builder.Property(f => f.ChangeReason).HasMaxLength(512);
        builder.Property(f => f.ConfirmedSource).HasConversion<string>().HasMaxLength(16);
        builder.Property(f => f.ReviewState).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(f => f.Document)
            .WithMany()
            .HasForeignKey(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
