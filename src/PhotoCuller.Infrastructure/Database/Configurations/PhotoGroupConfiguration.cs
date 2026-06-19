using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Database.Configurations;

internal sealed class PhotoGroupConfiguration : IEntityTypeConfiguration<PhotoGroup>
{
    public void Configure(EntityTypeBuilder<PhotoGroup> builder)
    {
        builder.ToTable("PhotoGroups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(g => g.GroupType)
            .HasConversion<int>()
            .HasDefaultValue(GroupType.Similar);

        builder.Property(g => g.SimilarityScore)
            .HasColumnType("REAL");

        builder.HasIndex(g => g.GroupType);
        builder.HasIndex(g => g.CreatedTime);
    }
}
