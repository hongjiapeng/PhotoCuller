using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoCuller.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddAestheticScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<float>(
                name: "AestheticScore",
                table: "Photos",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScoringModel",
                table: "Photos",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocalModelPreset",
                table: "AiModelProfiles",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "clip-aesthetic-0-1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AestheticScore",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ScoringModel",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "LocalModelPreset",
                table: "AiModelProfiles");
        }
    }
}
