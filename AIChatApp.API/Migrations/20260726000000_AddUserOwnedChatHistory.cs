using System;
using AIChatApp.Core.Data_Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIChatApp.API.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260726000000_AddUserOwnedChatHistory")]
    public partial class AddUserOwnedChatHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ChatId",
                table: "ChatMessageEntity",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "MessageId",
                table: "ChatMessageEntity",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "ChatMessageEntity",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "ChatMessageEntity",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChatId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageEntity_MessageId",
                table: "ChatMessageEntity",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageEntity_UserId_ChatId",
                table: "ChatMessageEntity",
                columns: new[] { "UserId", "ChatId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_UserId_ChatId",
                table: "ChatConversations",
                columns: new[] { "UserId", "ChatId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessageEntity_MessageId",
                table: "ChatMessageEntity");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessageEntity_UserId_ChatId",
                table: "ChatMessageEntity");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "ChatMessageEntity");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ChatMessageEntity");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "ChatMessageEntity");

            migrationBuilder.AlterColumn<string>(
                name: "ChatId",
                table: "ChatMessageEntity",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
        }
    }
}
