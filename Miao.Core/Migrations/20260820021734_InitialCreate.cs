using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Miao.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomLibraries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomLibraries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Novels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedTitle = table.Column<string>(type: "TEXT", nullable: false),
                    CustomTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedAuthor = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    SourceDescription = table.Column<string>(type: "TEXT", nullable: false),
                    CoverImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDownloaded = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastReadChapterNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Novels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Volumes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Volumes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chapters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VolumeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedTitle = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalContent = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayContent = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEditedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chapters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chapters_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsShared = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerNovelId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterGroups_Novels_OwnerNovelId",
                        column: x => x.OwnerNovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CustomLibraryNovels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomLibraryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomLibraryNovels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomLibraryNovels_CustomLibraries_CustomLibraryId",
                        column: x => x.CustomLibraryId,
                        principalTable: "CustomLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomLibraryNovels_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlossarySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsShared = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    OwnerNovelId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossarySets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlossarySets_Novels_OwnerNovelId",
                        column: x => x.OwnerNovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "NovelLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelLinks_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelSources_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelTags_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChapterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notes_Chapters_ChapterId",
                        column: x => x.ChapterId,
                        principalTable: "Chapters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterGroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_CharacterGroups_CharacterGroupId",
                        column: x => x.CharacterGroupId,
                        principalTable: "CharacterGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelCharacterGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterGroupId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelCharacterGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelCharacterGroups_CharacterGroups_CharacterGroupId",
                        column: x => x.CharacterGroupId,
                        principalTable: "CharacterGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelCharacterGroups_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GlossarySetEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GlossarySetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalTerm = table.Column<string>(type: "TEXT", nullable: false),
                    HanViet = table.Column<string>(type: "TEXT", nullable: true),
                    PinYin = table.Column<string>(type: "TEXT", nullable: true),
                    TranslatedTerm = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlossarySetEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlossarySetEntries_GlossarySets_GlossarySetId",
                        column: x => x.GlossarySetId,
                        principalTable: "GlossarySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NovelGlossarySets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NovelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GlossarySetId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NovelGlossarySets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NovelGlossarySets_GlossarySets_GlossarySetId",
                        column: x => x.GlossarySetId,
                        principalTable: "GlossarySets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NovelGlossarySets_Novels_NovelId",
                        column: x => x.NovelId,
                        principalTable: "Novels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterAliases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AliasText = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterAliases_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chapters_NovelId",
                table: "Chapters",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterAliases_CharacterId",
                table: "CharacterAliases",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterGroups_OwnerNovelId",
                table: "CharacterGroups",
                column: "OwnerNovelId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_CharacterGroupId",
                table: "Characters",
                column: "CharacterGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomLibraryNovels_CustomLibraryId",
                table: "CustomLibraryNovels",
                column: "CustomLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomLibraryNovels_NovelId",
                table: "CustomLibraryNovels",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_GlossarySetEntries_GlossarySetId",
                table: "GlossarySetEntries",
                column: "GlossarySetId");

            migrationBuilder.CreateIndex(
                name: "IX_GlossarySets_OwnerNovelId",
                table: "GlossarySets",
                column: "OwnerNovelId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_ChapterId",
                table: "Notes",
                column: "ChapterId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelCharacterGroups_CharacterGroupId",
                table: "NovelCharacterGroups",
                column: "CharacterGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelCharacterGroups_NovelId_CharacterGroupId",
                table: "NovelCharacterGroups",
                columns: new[] { "NovelId", "CharacterGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelGlossarySets_GlossarySetId",
                table: "NovelGlossarySets",
                column: "GlossarySetId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelGlossarySets_NovelId_GlossarySetId",
                table: "NovelGlossarySets",
                columns: new[] { "NovelId", "GlossarySetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NovelLinks_NovelId",
                table: "NovelLinks",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelSources_NovelId",
                table: "NovelSources",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelTags_NovelId",
                table: "NovelTags",
                column: "NovelId");

            migrationBuilder.CreateIndex(
                name: "IX_NovelTags_TagId",
                table: "NovelTags",
                column: "TagId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterAliases");

            migrationBuilder.DropTable(
                name: "CustomLibraryNovels");

            migrationBuilder.DropTable(
                name: "GlossarySetEntries");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "NovelCharacterGroups");

            migrationBuilder.DropTable(
                name: "NovelGlossarySets");

            migrationBuilder.DropTable(
                name: "NovelLinks");

            migrationBuilder.DropTable(
                name: "NovelSources");

            migrationBuilder.DropTable(
                name: "NovelTags");

            migrationBuilder.DropTable(
                name: "Volumes");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "CustomLibraries");

            migrationBuilder.DropTable(
                name: "Chapters");

            migrationBuilder.DropTable(
                name: "GlossarySets");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "CharacterGroups");

            migrationBuilder.DropTable(
                name: "Novels");
        }
    }
}
