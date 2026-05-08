using Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Documents;

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);

        builder.HasIndex(d => d.UserId);

        builder.Property(d => d.OriginalFileName).HasMaxLength(512).IsRequired();
        builder.Property(d => d.StoredFileName).HasMaxLength(512).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(64);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.EncryptedFilePath).HasMaxLength(1024).IsRequired();
        builder.Property(d => d.EncryptionIv).HasMaxLength(64).IsRequired();

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
