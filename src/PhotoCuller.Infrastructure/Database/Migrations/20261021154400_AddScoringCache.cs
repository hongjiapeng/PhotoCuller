using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PhotoCuller.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddScoringCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScoringSignature",
                table: "Photos",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScoringSignature",
                table: "Photos");
        }
    }
}
