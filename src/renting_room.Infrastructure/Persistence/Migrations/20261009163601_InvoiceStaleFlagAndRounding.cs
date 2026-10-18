using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceStaleFlagAndRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices");

            migrationBuilder.AddColumn<bool>(
                name: "round_invoice_total",
                table: "properties",
                type: "boolean",
                nullable: false,
                defaultValue: true); // BL-BR-29: mặc định bật — nháp hiện có làm tròn khi tính lại, phiếu đã chốt giữ nguyên

            migrationBuilder.AddColumn<bool>(
                name: "is_stale",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "round_total",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "rounding_amount",
                table: "invoices",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_stale_drafts",
                table: "invoices",
                columns: new[] { "organization_id", "property_id" },
                filter: "status = 'Draft' AND is_stale");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices",
                sql: "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total + rounding_amount");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_rounding",
                table: "invoices",
                sql: "rounding_amount > -1000 AND rounding_amount < 1000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_stale_drafts",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_rounding",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "round_invoice_total",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "is_stale",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "round_total",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "rounding_amount",
                table: "invoices");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refund",
                table: "invoices",
                sql: "refund_total <= 0 AND total_amount = subtotal + discount_total + refund_total");
        }
    }
}
