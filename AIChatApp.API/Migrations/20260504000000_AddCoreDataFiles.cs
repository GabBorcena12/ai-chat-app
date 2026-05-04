using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChatApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCoreDataFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CoreDataFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RelativePath = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ContentKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Area = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProfileId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StructuredJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoreDataFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CoreDataFiles_ContentKey",
                table: "CoreDataFiles",
                column: "ContentKey");

            migrationBuilder.CreateIndex(
                name: "IX_CoreDataFiles_RelativePath",
                table: "CoreDataFiles",
                column: "RelativePath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CoreDataFiles");
        }
    }
}
