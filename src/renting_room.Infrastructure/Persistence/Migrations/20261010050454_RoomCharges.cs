using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_settled",
                table: "invoice_lines",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "room_charge_id",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "room_charges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    incurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    is_settled = table.Column<bool>(type: "boolean", nullable: false),
                    settled_on = table.Column<DateOnly>(type: "date", nullable: true),
                    settled_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_charges", x => x.id);
                    table.CheckConstraint("ck_room_charges_amount", "amount > 0");
                    table.CheckConstraint("ck_room_charges_cancelled", "cancelled_at IS NULL OR (cancel_reason IS NOT NULL AND invoice_id IS NULL)");
                    table.CheckConstraint("ck_room_charges_settled", "NOT is_settled OR (settled_on IS NOT NULL AND settled_method IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_room_charges_contracts_organization_id_property_id_contract",
                        columns: x => new { x.organization_id, x.property_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_room_charges_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_room_charges_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_room_charge",
                table: "invoice_lines",
                column: "room_charge_id",
                filter: "room_charge_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_room_charges_invoice_id",
                table: "room_charges",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_room_charges_organization_id_property_id_contract_id",
                table: "room_charges",
                columns: new[] { "organization_id", "property_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_room_charges_pending",
                table: "room_charges",
                columns: new[] { "organization_id", "contract_id" },
                filter: "invoice_id IS NULL AND cancelled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_room_charges_room",
                table: "room_charges",
                columns: new[] { "organization_id", "room_id", "incurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_room_charges_room_id",
                table: "room_charges",
                column: "room_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "room_charges");

            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_room_charge",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "is_settled",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "room_charge_id",
                table: "invoice_lines");
        }
    }
}
