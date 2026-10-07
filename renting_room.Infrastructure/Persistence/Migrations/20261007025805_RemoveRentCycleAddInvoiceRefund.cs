using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRentCycleAddInvoiceRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BL-BR-26 đã bỏ: HĐ đang đóng tiền phòng nhiều tháng / lần mà xóa cột thì kỳ sau sẽ thu lại tiền đã đóng trước ⇒ dừng để xử lý tay.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM contracts WHERE rent_cycle_months > 1 AND status IN ('Draft', 'Active', 'Liquidating')) THEN
                        RAISE EXCEPTION 'Còn hợp đồng đóng tiền phòng nhiều tháng / lần — chuyển về 1 tháng và kiểm tra phiếu đã thu trước khi cập nhật.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_properties_rent_cycle",
                table: "properties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_note",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_rent_cycle",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "default_rent_cycle_months",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "rent_cycle_months",
                table: "contracts");

            migrationBuilder.AddColumn<string>(
                name: "refund_method",
                table: "invoices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "refund_note",
                table: "invoices",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "refund_total",
                table: "invoices",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "refunded_on",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices",
                sql: "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refunded",
                table: "invoices",
                sql: "refunded_on IS NULL OR (total_amount < 0 AND refund_method IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices",
                sql: "subtotal + discount_total >= 0 OR status = 'Draft'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines",
                sql: "line_type NOT IN ('ManualDiscount','Refund') OR amount <= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_note",
                table: "invoice_lines",
                sql: "line_type NOT IN ('Surcharge','ManualDiscount','Refund') OR note IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines",
                sql: "line_type IN ('ManualDiscount','Refund') OR amount >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refunded",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_note",
                table: "invoice_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "refund_method",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "refund_note",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "refund_total",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "refunded_on",
                table: "invoices");

            migrationBuilder.AddColumn<int>(
                name: "default_rent_cycle_months",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "rent_cycle_months",
                table: "contracts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_properties_rent_cycle",
                table: "properties",
                sql: "default_rent_cycle_months IN (1, 2, 3, 6, 12)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices",
                sql: "total_amount >= 0 OR status = 'Draft'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_discount",
                table: "invoice_lines",
                sql: "line_type <> 'ManualDiscount' OR amount <= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_note",
                table: "invoice_lines",
                sql: "line_type NOT IN ('Surcharge','ManualDiscount') OR note IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_lines_sign",
                table: "invoice_lines",
                sql: "line_type = 'ManualDiscount' OR amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_rent_cycle",
                table: "contracts",
                sql: "rent_cycle_months IN (1, 2, 3, 6, 12)");
        }
    }
}
