using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceRefundWaived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refunded",
                table: "invoices");

            migrationBuilder.AddColumn<bool>(
                name: "refund_waived",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refund_waived",
                table: "invoices",
                sql: "NOT refund_waived OR (refunded_on IS NOT NULL AND refund_method IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refunded",
                table: "invoices",
                sql: "refunded_on IS NULL OR (total_amount < 0 AND (refund_method IS NOT NULL OR refund_waived))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refund_waived",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_refunded",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "refund_waived",
                table: "invoices");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_refunded",
                table: "invoices",
                sql: "refunded_on IS NULL OR (total_amount < 0 AND refund_method IS NOT NULL)");
        }
    }
}
