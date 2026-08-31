using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddGlossaryGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlossaryGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsShared = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlossaryGroupGlossarySet",
                columns: table => new
                {
                    GroupsId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SetsId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossaryGroupGlossarySet", x => new { x.GroupsId, x.SetsId });
                    table.ForeignKey(
                        name: "FK_GlossaryGroupGlossarySet_GlossaryGroups_GroupsId",
                        column: x => x.GroupsId,
                        principalTable: "GlossaryGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GlossaryGroupGlossarySet_GlossarySets_SetsId",
                        column: x => x.SetsId,
                        principalTable: "GlossarySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlossaryGroupGlossarySet_SetsId",
                table: "GlossaryGroupGlossarySet",
                column: "SetsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlossaryGroupGlossarySet");

            migrationBuilder.DropTable(
                name: "GlossaryGroups");
        }
    }
}
