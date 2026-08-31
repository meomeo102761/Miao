using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddBannerScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BannerScale",
                table: "CharacterGroups",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BannerScale",
                table: "CharacterGroups");
        }
    }
}
