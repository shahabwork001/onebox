using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignTemplateVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TemplateVariables",
                schema: "centralchat",
                table: "Campaigns",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TemplateVariables",
                schema: "centralchat",
                table: "Campaigns");
        }
    }
}
