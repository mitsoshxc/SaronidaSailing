using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Infrastructure.Data.Configurations;

public sealed class ContentHistoryConfiguration : IEntityTypeConfiguration<ContentHistory>
{
    public void Configure(EntityTypeBuilder<ContentHistory> builder)
    {
        builder.ToTable("ContentHistory");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Value).IsRequired();
        builder.Property(x => x.ChangedBy).HasMaxLength(256).IsRequired();

        builder.HasIndex(x => x.ContentItemId).HasDatabaseName("IX_ContentHistory_ContentItemId");
        builder.HasIndex(x => x.SaveId).HasDatabaseName("IX_ContentHistory_SaveId");

        builder.HasOne<ContentItem>()
            .WithMany()
            .HasForeignKey(x => x.ContentItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
