using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChatApp.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBackofficeContentAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PromotedKnowledgeEntryId",
                table: "ChatResponseReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewCategory",
                table: "ChatResponseReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewNotes",
                table: "ChatResponseReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "ChatResponseReports",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "ChatResponseReports",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "ChatResponseReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidatedQuestion",
                table: "ChatResponseReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ValidatedResponse",
                table: "ChatResponseReports",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssistantKnowledgeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AliasesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    KeywordsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantKnowledgeEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistantPromptTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistantPromptTemplates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistantKnowledgeEntries");

            migrationBuilder.DropTable(
                name: "AssistantPromptTemplates");

            migrationBuilder.DropColumn(
                name: "PromotedKnowledgeEntryId",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ReviewCategory",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ReviewNotes",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ValidatedQuestion",
                table: "ChatResponseReports");

            migrationBuilder.DropColumn(
                name: "ValidatedResponse",
                table: "ChatResponseReports");
        }
    }
}
