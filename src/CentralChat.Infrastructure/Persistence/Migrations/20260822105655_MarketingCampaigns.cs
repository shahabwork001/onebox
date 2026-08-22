using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketingCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MarketingOptOut",
                schema: "centralchat",
                table: "Contacts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MarketingOptOutAt",
                schema: "centralchat",
                table: "Contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CampaignRecipients",
                schema: "centralchat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContactId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExternalMessageId = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignRecipients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                schema: "centralchat",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TemplateName = table.Column<string>(type: "text", nullable: false),
                    TemplateLanguage = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TotalRecipients = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_MarketingOptOut",
                schema: "centralchat",
                table: "Contacts",
                column: "MarketingOptOut");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_CampaignId_ContactId",
                schema: "centralchat",
                table: "CampaignRecipients",
                columns: new[] { "CampaignId", "ContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_ExternalMessageId",
                schema: "centralchat",
                table: "CampaignRecipients",
                column: "ExternalMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CreatedAt",
                schema: "centralchat",
                table: "Campaigns",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignRecipients",
                schema: "centralchat");

            migrationBuilder.DropTable(
                name: "Campaigns",
                schema: "centralchat");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_MarketingOptOut",
                schema: "centralchat",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "MarketingOptOut",
                schema: "centralchat",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "MarketingOptOutAt",
                schema: "centralchat",
                table: "Contacts");
        }
    }
}
