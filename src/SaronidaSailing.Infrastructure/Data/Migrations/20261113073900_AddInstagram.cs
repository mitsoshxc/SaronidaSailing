using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaronidaSailing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "MediaAssets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Upload");

            migrationBuilder.CreateTable(
                name: "InstagramPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstagramId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Permalink = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: true),
                    MediaType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MediaAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstagramPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstagramPosts_MediaAssets_MediaAssetId",
                        column: x => x.MediaAssetId,
                        principalTable: "MediaAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstagramPosts_MediaAssetId",
                table: "InstagramPosts",
                column: "MediaAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_InstagramPosts_PostedAt",
                table: "InstagramPosts",
                column: "PostedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "UX_InstagramPosts_InstagramId",
                table: "InstagramPosts",
                column: "InstagramId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_IntegrationSettings_Key",
                table: "IntegrationSettings",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstagramPosts");

            migrationBuilder.DropTable(
                name: "IntegrationSettings");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MediaAssets");
        }
    }
}
