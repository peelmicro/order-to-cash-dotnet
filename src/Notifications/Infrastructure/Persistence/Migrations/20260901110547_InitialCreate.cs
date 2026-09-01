using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderToCash.Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    consumer = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_processed_events_event_id_consumer",
                table: "processed_events",
                columns: new[] { "event_id", "consumer" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_events");
        }
    }
}
