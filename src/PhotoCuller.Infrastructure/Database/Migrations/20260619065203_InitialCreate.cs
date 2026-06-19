using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoCuller.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiModelProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ProviderType = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelType = table.Column<int>(type: "INTEGER", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    EncryptedApiKey = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxConcurrency = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    LocalModelPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    EmbeddingDimension = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportedTasks = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    OperationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUndoable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileOperationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PhotoGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupType = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    RecommendedPhotoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PhotoCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SimilarityScore = table.Column<float>(type: "REAL", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExifTakenTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Sha256Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PerceptualHash = table.Column<ulong>(type: "INTEGER", nullable: true),
                    ThumbnailPath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    OverallScore = table.Column<float>(type: "REAL", nullable: true),
                    SharpnessScore = table.Column<float>(type: "REAL", nullable: true),
                    ExposureScore = table.Column<float>(type: "REAL", nullable: true),
                    ContrastScore = table.Column<float>(type: "REAL", nullable: true),
                    NoiseScore = table.Column<float>(type: "REAL", nullable: true),
                    ResolutionScore = table.Column<float>(type: "REAL", nullable: true),
                    CompositionScore = table.Column<float>(type: "REAL", nullable: true),
                    ColorScore = table.Column<float>(type: "REAL", nullable: true),
                    FaceQualityScore = table.Column<float>(type: "REAL", nullable: true),
                    RecommendationStatus = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    ScanTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AnalysisVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsMovedToTrash = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Photos_PhotoGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "PhotoGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelProfiles_IsEnabled",
                table: "AiModelProfiles",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelProfiles_ProviderType",
                table: "AiModelProfiles",
                column: "ProviderType");

            migrationBuilder.CreateIndex(
                name: "IX_FileOperationLogs_IsUndoable_Status",
                table: "FileOperationLogs",
                columns: new[] { "IsUndoable", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileOperationLogs_OperationTime",
                table: "FileOperationLogs",
                column: "OperationTime");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoGroups_CreatedTime",
                table: "PhotoGroups",
                column: "CreatedTime");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoGroups_GroupType",
                table: "PhotoGroups",
                column: "GroupType");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_FilePath",
                table: "Photos",
                column: "FilePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_GroupId",
                table: "Photos",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_RecommendationStatus",
                table: "Photos",
                column: "RecommendationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_ScanTime",
                table: "Photos",
                column: "ScanTime");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_Sha256Hash",
                table: "Photos",
                column: "Sha256Hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiModelProfiles");

            migrationBuilder.DropTable(
                name: "FileOperationLogs");

            migrationBuilder.DropTable(
                name: "Photos");

            migrationBuilder.DropTable(
                name: "PhotoGroups");
        }
    }
}
