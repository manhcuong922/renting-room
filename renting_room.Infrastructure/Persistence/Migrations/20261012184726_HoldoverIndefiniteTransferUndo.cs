using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HoldoverIndefiniteTransferUndo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "previous_end_date",
                table: "contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "undo_snapshot",
                table: "contract_room_moves",
                type: "jsonb",
                nullable: true);

            // CT-BR-45 (13/10/2026): HĐ đang "ở tiếp, chưa ký lại" chuyển hẳn sang không thời hạn — giữ hạn cũ ở previous_end_date.
            migrationBuilder.Sql(
                "UPDATE contracts SET previous_end_date = end_date, end_date = NULL " +
                "WHERE holdover_since IS NOT NULL AND end_date IS NOT NULL AND actual_end_date IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE contracts SET end_date = previous_end_date WHERE previous_end_date IS NOT NULL AND end_date IS NULL;");

            migrationBuilder.DropColumn(
                name: "previous_end_date",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "undo_snapshot",
                table: "contract_room_moves");
        }
    }
}
