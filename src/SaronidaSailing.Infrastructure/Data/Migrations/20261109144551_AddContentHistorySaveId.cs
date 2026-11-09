using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaronidaSailing.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHistorySaveId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SaveId",
                table: "ContentHistory",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE \"ContentHistory\" SET \"SaveId\" = \"Id\";");

            migrationBuilder.CreateIndex(
                name: "IX_ContentHistory_SaveId",
                table: "ContentHistory",
                column: "SaveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ContentHistory_SaveId",
                table: "ContentHistory");

            migrationBuilder.DropColumn(
                name: "SaveId",
                table: "ContentHistory");
        }
    }
}
