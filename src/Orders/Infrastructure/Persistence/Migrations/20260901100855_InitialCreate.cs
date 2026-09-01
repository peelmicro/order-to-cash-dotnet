using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderToCash.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    iso_number = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    symbol = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    decimal_points = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_currencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_number_sequences",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    next_value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_number_sequences", x => x.id);
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
                name: "saga_commands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    command = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    triggering_event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "pending"),
                    attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saga_commands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saga_ignored_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    observed_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    expected_status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    marker = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saga_ignored_facts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    vat = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    gln = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    currency_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disabled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.id);
                    table.ForeignKey(
                        name: "FK_companies_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ean = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    price = table.Column<int>(type: "int", nullable: false),
                    currency_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disabled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                    table.ForeignKey(
                        name: "FK_products_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "retailers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    vat = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    gln = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    currency_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    disabled_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retailers", x => x.id);
                    table.ForeignKey(
                        name: "FK_retailers_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    order_date = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    retailer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    currency_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initial_amount = table.Column<int>(type: "int", nullable: false),
                    initial_discount = table.Column<int>(type: "int", nullable: false),
                    total_amount = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    cancellation_reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_orders", x => x.id);
                    table.ForeignKey(
                        name: "FK_orders_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_orders_retailers_retailer_id",
                        column: x => x.retailer_id,
                        principalTable: "retailers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    price = table.Column<int>(type: "int", nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    discount = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_order_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_companies_code",
                table: "companies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_companies_currency_id",
                table: "companies",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "IX_currencies_code",
                table: "currencies",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_items_order_id",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_items_product_id",
                table: "order_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_company_id",
                table: "orders",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_currency_id",
                table: "orders",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "IX_orders_order_reference",
                table: "orders",
                column: "order_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_retailer_id_status",
                table: "orders",
                columns: new[] { "retailer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_orders_status_order_date",
                table: "orders",
                columns: new[] { "status", "order_date" });

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
                name: "IX_products_code",
                table: "products",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_currency_id",
                table: "products",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_ean",
                table: "products",
                column: "ean",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_retailers_code",
                table: "retailers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_retailers_currency_id",
                table: "retailers",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "IX_saga_commands_order_id_command",
                table: "saga_commands",
                columns: new[] { "order_id", "command" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_saga_commands_status_created_at",
                table: "saga_commands",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_saga_commands_status_next_attempt_at",
                table: "saga_commands",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_saga_ignored_facts_correlation_id",
                table: "saga_ignored_facts",
                column: "correlation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_items");

            migrationBuilder.DropTable(
                name: "order_number_sequences");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "saga_commands");

            migrationBuilder.DropTable(
                name: "saga_ignored_facts");

            migrationBuilder.DropTable(
                name: "orders");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "retailers");

            migrationBuilder.DropTable(
                name: "currencies");
        }
    }
}
