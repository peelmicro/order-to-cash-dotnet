using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderToCash.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    retailer_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    company_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    credit_limit = table.Column<int>(type: "int", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credits", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoice_number_sequences",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    next_value = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_number_sequences", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    invoice_date = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    company_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    retailer_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    order_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<int>(type: "int", nullable: false),
                    discount = table.Column<int>(type: "int", nullable: false),
                    total_amount = table.Column<int>(type: "int", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.id);
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
                name: "credit_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    credit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    order_reference = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    amount = table.Column<int>(type: "int", nullable: false),
                    type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    credit_date = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credit_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_credit_items_credits_credit_id",
                        column: x => x.credit_id,
                        principalTable: "credits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    product_code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    units = table.Column<int>(type: "int", nullable: false),
                    price = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    payment_reference = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<int>(type: "int", nullable: false),
                    currency_code = table.Column<string>(type: "char(3)", nullable: false),
                    value_date = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_credit_items_credit_id_order_reference",
                table: "credit_items",
                columns: new[] { "credit_id", "order_reference" });

            migrationBuilder.CreateIndex(
                name: "IX_credits_code",
                table: "credits",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_credits_retailer_code_company_code",
                table: "credits",
                columns: new[] { "retailer_code", "company_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoice_items_invoice_id",
                table: "invoice_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_invoices_invoice_reference",
                table: "invoices",
                column: "invoice_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_order_reference",
                table: "invoices",
                column: "order_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invoices_status_invoice_date",
                table: "invoices",
                columns: new[] { "status", "invoice_date" });

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
                name: "IX_payments_invoice_id",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_payments_payment_reference",
                table: "payments",
                column: "payment_reference",
                unique: true);

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
                name: "credit_items");

            migrationBuilder.DropTable(
                name: "invoice_items");

            migrationBuilder.DropTable(
                name: "invoice_number_sequences");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "credits");

            migrationBuilder.DropTable(
                name: "invoices");
        }
    }
}
