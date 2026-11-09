using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Infrastructure.Data.Configurations;

public sealed class ContentSectionConfiguration : IEntityTypeConfiguration<ContentSection>
{
    public void Configure(EntityTypeBuilder<ContentSection> builder)
    {
        builder.ToTable("ContentSections");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PageKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SectionKey).HasMaxLength(100).IsRequired();

        builder.HasIndex(x => new { x.PageKey, x.SectionKey })
            .IsUnique()
            .HasDatabaseName("UX_ContentSections_PageKey_SectionKey");
    }
}
