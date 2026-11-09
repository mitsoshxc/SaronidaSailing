using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Infrastructure.Data.Configurations;

public sealed class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("ContentItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PageKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SectionKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FieldKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.UpdatedBy).HasMaxLength(256).IsRequired();

        builder.HasIndex(x => new { x.PageKey, x.SectionKey, x.FieldKey, x.Locale })
            .IsUnique()
            .HasFilter("\"ListItemId\" IS NULL")
            .HasDatabaseName("UX_ContentItems_Standalone");

        builder.HasIndex(x => new { x.ListItemId, x.FieldKey, x.Locale })
            .IsUnique()
            .HasFilter("\"ListItemId\" IS NOT NULL")
            .HasDatabaseName("UX_ContentItems_ListField");

        builder.HasIndex(x => new { x.PageKey, x.Locale })
            .HasDatabaseName("IX_ContentItems_PageKey_Locale");

        builder.HasOne(x => x.ListItem)
            .WithMany(x => x.Fields)
            .HasForeignKey(x => x.ListItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
