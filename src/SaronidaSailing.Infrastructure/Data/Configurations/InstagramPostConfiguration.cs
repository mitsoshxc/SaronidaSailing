using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SaronidaSailing.Core.Entities;

namespace SaronidaSailing.Infrastructure.Data.Configurations;

public sealed class InstagramPostConfiguration : IEntityTypeConfiguration<InstagramPost>
{
    public void Configure(EntityTypeBuilder<InstagramPost> builder)
    {
        builder.ToTable("InstagramPosts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstagramId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Permalink).IsRequired();
        builder.Property(x => x.MediaType).HasMaxLength(30).IsRequired();

        builder.HasIndex(x => x.InstagramId)
            .IsUnique()
            .HasDatabaseName("UX_InstagramPosts_InstagramId");

        builder.HasIndex(x => x.PostedAt)
            .IsDescending()
            .HasDatabaseName("IX_InstagramPosts_PostedAt");

        builder.HasOne<MediaAsset>()
            .WithMany()
            .HasForeignKey(x => x.MediaAssetId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
