using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentralChat.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TicketLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                schema: "centralchat",
                table: "Tickets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                schema: "centralchat",
                table: "Tickets");
        }
    }
}
