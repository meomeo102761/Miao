using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterContentSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CharacterDescriptionSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterDescriptionSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterDescriptionSections_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterInfoSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterInfoSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterInfoSections_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterDescriptionBlocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterDescriptionSectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    TextContent = table.Column<string>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterDescriptionBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterDescriptionBlocks_CharacterDescriptionSections_CharacterDescriptionSectionId",
                        column: x => x.CharacterDescriptionSectionId,
                        principalTable: "CharacterDescriptionSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterInfoEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterInfoSectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterInfoEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterInfoEntries_CharacterInfoSections_CharacterInfoSectionId",
                        column: x => x.CharacterInfoSectionId,
                        principalTable: "CharacterInfoSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterDescriptionBlocks_CharacterDescriptionSectionId",
                table: "CharacterDescriptionBlocks",
                column: "CharacterDescriptionSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterDescriptionSections_CharacterId",
                table: "CharacterDescriptionSections",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterInfoEntries_CharacterInfoSectionId",
                table: "CharacterInfoEntries",
                column: "CharacterInfoSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterInfoSections_CharacterId",
                table: "CharacterInfoSections",
                column: "CharacterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterDescriptionBlocks");

            migrationBuilder.DropTable(
                name: "CharacterInfoEntries");

            migrationBuilder.DropTable(
                name: "CharacterDescriptionSections");

            migrationBuilder.DropTable(
                name: "CharacterInfoSections");
        }
    }
}
