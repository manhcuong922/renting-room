using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransferMeterClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_meter_readings_final",
                table: "meter_readings");

            migrationBuilder.DropIndex(
                name: "ux_meter_readings_handover",
                table: "meter_readings");

            migrationBuilder.AddColumn<Guid>(
                name: "end_reading_id",
                table: "room_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fee_type_id",
                table: "room_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "meter_id",
                table: "room_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "quantity",
                table: "room_charges",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "start_reading_id",
                table: "room_charges",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_price",
                table: "room_charges",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_charges_meter_closing",
                table: "room_charges",
                column: "meter_id",
                filter: "meter_id IS NOT NULL AND cancelled_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_charges_meter_closing",
                table: "room_charges",
                sql: "meter_id IS NULL OR (start_reading_id IS NOT NULL AND end_reading_id IS NOT NULL AND fee_type_id IS NOT NULL AND quantity IS NOT NULL AND unit_price IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_final",
                table: "meter_readings",
                columns: new[] { "meter_id", "contract_id", "reading_date" },
                unique: true,
                filter: "kind = 'Final' AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_handover",
                table: "meter_readings",
                columns: new[] { "meter_id", "contract_id", "reading_date" },
                unique: true,
                filter: "kind = 'Handover' AND voided_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_room_charges_meter_closing",
                table: "room_charges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_charges_meter_closing",
                table: "room_charges");

            migrationBuilder.DropIndex(
                name: "ux_meter_readings_final",
                table: "meter_readings");

            migrationBuilder.DropIndex(
                name: "ux_meter_readings_handover",
                table: "meter_readings");

            migrationBuilder.DropColumn(
                name: "end_reading_id",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "fee_type_id",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "meter_id",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "quantity",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "start_reading_id",
                table: "room_charges");

            migrationBuilder.DropColumn(
                name: "unit_price",
                table: "room_charges");

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_final",
                table: "meter_readings",
                columns: new[] { "meter_id", "contract_id" },
                unique: true,
                filter: "kind = 'Final' AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_handover",
                table: "meter_readings",
                columns: new[] { "meter_id", "contract_id" },
                unique: true,
                filter: "kind = 'Handover' AND voided_at IS NULL");
        }
    }
}
