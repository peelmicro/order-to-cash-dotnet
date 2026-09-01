using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "despatch_number_sequences",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    next_value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despatch_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "despatches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    despatch_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    despatch_date = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    company_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    retailer_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    order_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despatches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    causation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    published_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    trace_parent = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "stock",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    units = table.Column<int>(type: "int", nullable: false),
                    reserved_units = table.Column<int>(type: "int", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "despatch_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    despatch_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    units = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_despatch_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_despatch_items_despatches_despatch_id",
                        column: x => x.despatch_id,
                        principalTable: "despatches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stock_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    retailer_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    order_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    units = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.id);
                    table.ForeignKey(
                        name: "FK_reservations_stock_stock_id",
                        column: x => x.stock_id,
                        principalTable: "stock",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_despatch_items_despatch_id",
                table: "despatch_items",
                column: "despatch_id");

            migrationBuilder.CreateIndex(
                name: "IX_despatches_despatch_reference",
                table: "despatches",
                column: "despatch_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_despatches_order_reference",
                table: "despatches",
                column: "order_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_event_id",
                table: "outbox",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_published_at_occurred_at",
                table: "outbox",
                columns: new[] { "published_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_published_at_seq",
                table: "outbox",
                columns: new[] { "published_at", "seq" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_seq",
                table: "outbox",
                column: "seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processed_events_event_id_consumer",
                table: "processed_events",
                columns: new[] { "event_id", "consumer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_reservations_order_reference_status",
                table: "reservations",
                columns: new[] { "order_reference", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_stock_id",
                table: "reservations",
                column: "stock_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_company_code_product_code",
                table: "stock",
                columns: new[] { "company_code", "product_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "despatch_items");

            migrationBuilder.DropTable(
                name: "despatch_number_sequences");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "despatches");

            migrationBuilder.DropTable(
                name: "stock");
        }
    }
}
