using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderToCash.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_orders_request_id",
                table: "orders",
                column: "request_id",
                unique: true,
                filter: "[request_id] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_orders_request_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "orders");
        }
    }
}
