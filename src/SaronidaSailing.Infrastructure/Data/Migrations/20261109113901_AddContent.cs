using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaronidaSailing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentListItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ListKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentListItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SectionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentSections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SectionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FieldKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Locale = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    ListItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentItems_ContentListItems_ListItemId",
                        column: x => x.ListItemId,
                        principalTable: "ContentListItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContentHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentHistory_ContentItems_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentHistory_ContentItemId",
                table: "ContentHistory",
                column: "ContentItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_PageKey_Locale",
                table: "ContentItems",
                columns: new[] { "PageKey", "Locale" });

            migrationBuilder.CreateIndex(
                name: "UX_ContentItems_ListField",
                table: "ContentItems",
                columns: new[] { "ListItemId", "FieldKey", "Locale" },
                unique: true,
                filter: "\"ListItemId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_ContentItems_Standalone",
                table: "ContentItems",
                columns: new[] { "PageKey", "SectionKey", "FieldKey", "Locale" },
                unique: true,
                filter: "\"ListItemId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ContentListItems_PageKey_ListKey_SortOrder",
                table: "ContentListItems",
                columns: new[] { "PageKey", "ListKey", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_ContentSections_PageKey_SectionKey",
                table: "ContentSections",
                columns: new[] { "PageKey", "SectionKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentHistory");

            migrationBuilder.DropTable(
                name: "ContentSections");

            migrationBuilder.DropTable(
                name: "ContentItems");

            migrationBuilder.DropTable(
                name: "ContentListItems");
        }
    }
}
