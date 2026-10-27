using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalInvoiceSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines");

            migrationBuilder.AddColumn<decimal>(
                name: "carried_over_amount",
                table: "invoices",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "carried_to_invoice_id",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "settlement_total",
                table: "invoices",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_carried_over",
                table: "invoices",
                sql: "carried_over_amount >= 0 AND written_off_amount + carried_over_amount <= paid_amount AND (carried_over_amount = 0) = (carried_to_invoice_id IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices",
                sql: "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total + settlement_total + rounding_amount");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_settlement",
                table: "invoices",
                sql: "settlement_total = 0 OR invoice_type = 'Final'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines",
                sql: "line_type NOT IN ('ManualDiscount','Refund','Deposit') OR amount <= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines",
                sql: "line_type IN ('ManualDiscount','Refund','Deposit') OR amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_carried_over",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_settlement",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "carried_over_amount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "carried_to_invoice_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "settlement_total",
                table: "invoices");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices",
                sql: "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total + rounding_amount");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines",
                sql: "line_type NOT IN ('ManualDiscount','Refund') OR amount <= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines",
                sql: "line_type IN ('ManualDiscount','Refund') OR amount >= 0");
        }
    }
}
