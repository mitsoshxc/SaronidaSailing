using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Infrastructure.Data.Configurations;

public sealed class ContentListItemConfiguration : IEntityTypeConfiguration<ContentListItem>
{
    public void Configure(EntityTypeBuilder<ContentListItem> builder)
    {
        builder.ToTable("ContentListItems");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PageKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ListKey).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => new { x.PageKey, x.ListKey, x.SortOrder })
            .HasDatabaseName("IX_ContentListItems_PageKey_ListKey_SortOrder");
    }
}
