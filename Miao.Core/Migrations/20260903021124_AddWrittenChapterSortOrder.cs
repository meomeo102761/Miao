using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddWrittenChapterSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "WrittenNovels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "WrittenNovels");
        }
    }
}
