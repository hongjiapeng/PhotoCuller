using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Database.Configurations;

internal sealed class AiModelProfileConfiguration : IEntityTypeConfiguration<AiModelProfile>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<AiModelProfile> builder)
    {
        builder.ToTable("AiModelProfiles");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(m => m.ProviderType)
            .HasConversion<int>();

        builder.Property(m => m.ModelType)
            .HasConversion<int>();

        builder.Property(m => m.BaseUrl)
            .HasMaxLength(2048);

        // Encrypted API key (DPAPI ciphertext, base64)
        builder.Property(m => m.EncryptedApiKey)
            .HasMaxLength(4096);

        builder.Property(m => m.ModelId)
            .HasMaxLength(256);

        builder.Property(m => m.LocalModelPath)
            .HasMaxLength(1024);

        builder.Property(m => m.LocalModelPreset)
            .IsRequired()
            .HasMaxLength(64)
            .HasDefaultValue("clip-aesthetic-0-1");

        builder.Property(m => m.SupportedTasks)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>())
            .HasColumnType("TEXT");

        builder.HasIndex(m => m.ProviderType);
        builder.HasIndex(m => m.IsEnabled);
    }
}
