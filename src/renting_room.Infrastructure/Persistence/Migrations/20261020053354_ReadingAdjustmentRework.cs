using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReadingAdjustmentRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices");

            migrationBuilder.AddColumn<Guid>(
                name: "source_invoice_id",
                table: "room_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "source_usage_start",
                table: "room_charges",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_adjusted",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "needs_resend",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "price_snapshot",
                table: "invoice_lines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_reading_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usage_start = table.Column<DateOnly>(type: "date", nullable: false),
                    old_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    new_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    old_quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    new_quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    old_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    new_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    difference = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    handling = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    room_charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_adjustments_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_room_charges_reading_adjustment",
                table: "room_charges",
                columns: new[] { "source_invoice_id", "fee_type_id", "source_usage_start" },
                unique: true,
                filter: "source_invoice_id IS NOT NULL AND cancelled_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_charges_reading_adjustment",
                table: "room_charges",
                sql: "source_invoice_id IS NULL OR (fee_type_id IS NOT NULL AND source_usage_start IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_needs_resend",
                table: "invoices",
                columns: new[] { "organization_id", "property_id" },
                filter: "needs_resend");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices",
                sql: "subtotal + discount_total >= 0 OR status = 'Draft' OR invoice_type = 'Final'");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_adjustments_invoice",
                table: "invoice_adjustments",
                columns: new[] { "organization_id", "invoice_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_adjustments");

            migrationBuilder.DropIndex(
                name: "ux_room_charges_reading_adjustment",
                table: "room_charges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_charges_reading_adjustment",
                table: "room_charges");

            migrationBuilder.DropIndex(
                name: "ix_invoices_needs_resend",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "source_invoice_id",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "source_usage_start",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "is_adjusted",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "needs_resend",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "price_snapshot",
                table: "invoice_lines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_total",
                table: "invoices",
                sql: "subtotal + discount_total >= 0 OR status = 'Draft'");
        }
    }
}
