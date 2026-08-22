using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatMessageMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MediaId",
                schema: "centralchat",
                table: "ChatMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MediaSizeBytes",
                schema: "centralchat",
                table: "ChatMessages",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaId",
                schema: "centralchat",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "MediaSizeBytes",
                schema: "centralchat",
                table: "ChatMessages");
        }
    }
}
