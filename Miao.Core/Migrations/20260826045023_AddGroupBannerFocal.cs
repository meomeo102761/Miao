using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupBannerFocal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BannerFocalX",
                table: "CharacterGroups",
                type: "REAL",
                nullable: false,
                defaultValue: 0.5);

            migrationBuilder.AddColumn<double>(
                name: "BannerFocalY",
                table: "CharacterGroups",
                type: "REAL",
                nullable: false,
                defaultValue: 0.5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannerFocalX",
                table: "CharacterGroups");

            migrationBuilder.DropColumn(
                name: "BannerFocalY",
                table: "CharacterGroups");
        }
    }
}
