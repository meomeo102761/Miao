using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterFaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FactionId",
                table: "Characters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CharacterFactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterFactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterFactions_CharacterGroups_CharacterGroupId",
                        column: x => x.CharacterGroupId,
                        principalTable: "CharacterGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_FactionId",
                table: "Characters",
                column: "FactionId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterFactions_CharacterGroupId",
                table: "CharacterFactions",
                column: "CharacterGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Characters_CharacterFactions_FactionId",
                table: "Characters",
                column: "FactionId",
                principalTable: "CharacterFactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Characters_CharacterFactions_FactionId",
                table: "Characters");

            migrationBuilder.DropTable(
                name: "CharacterFactions");

            migrationBuilder.DropIndex(
                name: "IX_Characters_FactionId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "FactionId",
                table: "Characters");
        }
    }
}
