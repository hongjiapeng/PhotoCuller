using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoCuller.Core.Enums;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Infrastructure.Database.Configurations;

internal sealed class FileOperationLogConfiguration : IEntityTypeConfiguration<FileOperationLog>
{
    public void Configure(EntityTypeBuilder<FileOperationLog> builder)
    {
        builder.ToTable("FileOperationLogs");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.SourcePath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(l => l.TargetPath)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(l => l.OperationType)
            .HasConversion<int>();

        builder.Property(l => l.Status)
            .HasConversion<int>()
            .HasDefaultValue(OperationStatus.Success);

        builder.Property(l => l.ErrorMessage)
            .HasMaxLength(2048);

        builder.HasIndex(l => l.OperationTime);
        builder.HasIndex(l => new { l.IsUndoable, l.Status });
    }
}
