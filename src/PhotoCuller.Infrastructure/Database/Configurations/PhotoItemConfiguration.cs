using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Database.Configurations;

internal sealed class PhotoItemConfiguration : IEntityTypeConfiguration<PhotoItem>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<PhotoItem> builder)
    {
        builder.ToTable("Photos");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FilePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(p => p.FileName)
            .IsRequired()
            .HasMaxLength(260);

        builder.Property(p => p.FileExtension)
            .HasMaxLength(16);

        builder.Property(p => p.Sha256Hash)
            .HasMaxLength(64);

        // PerceptualHash stored as unsigned 64-bit int (fast bitwise Hamming distance)
        builder.Property(p => p.PerceptualHash)
            .HasColumnType("INTEGER");

        builder.Property(p => p.ThumbnailPath)
            .HasMaxLength(1024);

        builder.Property(p => p.ScoringModel)
            .HasMaxLength(512);

        builder.Property(p => p.ScoringSignature)
            .HasMaxLength(64);

        builder.Property(p => p.RecommendationStatus)
            .HasConversion<int>()
            .HasDefaultValue(RecommendationStatus.Unprocessed);

        // Tags: JSON array → TEXT column. EF value converter keeps the model clean.
        builder.Property(p => p.Tags)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .HasColumnType("TEXT")
            .HasDefaultValue(new List<string>());

        builder.Property(p => p.ErrorMessage)
            .HasMaxLength(2048);

        // Indexes for common query patterns
        builder.HasIndex(p => p.FilePath).IsUnique();
        builder.HasIndex(p => p.Sha256Hash);
        builder.HasIndex(p => p.GroupId);
        builder.HasIndex(p => p.RecommendationStatus);
        builder.HasIndex(p => p.ScanTime);

        // Navigation
        builder.HasOne(p => p.Group)
            .WithMany(g => g.Photos)
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
