using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderToCash.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSagaCommandsDeadLetterColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "dead_lettered_at",
                table: "saga_commands",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "triggering_event_envelope",
                table: "saga_commands",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "triggering_event_topic",
                table: "saga_commands",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dead_lettered_at",
                table: "saga_commands");

            migrationBuilder.DropColumn(
                name: "triggering_event_envelope",
                table: "saga_commands");

            migrationBuilder.DropColumn(
                name: "triggering_event_topic",
                table: "saga_commands");
        }
    }
}
