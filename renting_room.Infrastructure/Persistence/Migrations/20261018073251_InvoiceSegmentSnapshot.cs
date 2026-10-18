using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceSegmentSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_invoice_lines_fee",
                table: "invoice_lines");

            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "invoice_meter_segments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "meter_serial",
                table: "invoice_meter_segments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "room_code",
                table: "invoice_meter_segments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "room_id",
                table: "invoice_meter_segments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "start_date",
                table: "invoice_meter_segments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "usage_start",
                table: "invoice_meter_segments",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_fee",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "fee_type_id", "service_from" },
                unique: true,
                filter: "fee_type_id IS NOT NULL AND is_system");

            // Đoạn đo đã có: điền bản chụp phòng / số seri / ngày 2 chỉ số từ dữ liệu hiện tại (usage_start để NULL — phiếu cũ mỗi khoản 1 dòng).
            migrationBuilder.Sql("""
                UPDATE invoice_meter_segments s
                SET room_id = m.room_id, room_code = r.code, meter_serial = m.serial_no,
                    start_date = (SELECT reading_date FROM meter_readings WHERE id = s.start_reading_id),
                    end_date = (SELECT reading_date FROM meter_readings WHERE id = s.end_reading_id)
                FROM meters m JOIN rooms r ON r.id = m.room_id
                WHERE m.id = s.meter_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_invoice_lines_fee",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "invoice_meter_segments");

            migrationBuilder.DropColumn(
                name: "meter_serial",
                table: "invoice_meter_segments");

            migrationBuilder.DropColumn(
                name: "room_code",
                table: "invoice_meter_segments");

            migrationBuilder.DropColumn(
                name: "room_id",
                table: "invoice_meter_segments");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "invoice_meter_segments");

            migrationBuilder.DropColumn(
                name: "usage_start",
                table: "invoice_meter_segments");

            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_fee",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "fee_type_id" },
                unique: true,
                filter: "fee_type_id IS NOT NULL AND is_system");
        }
    }
}
