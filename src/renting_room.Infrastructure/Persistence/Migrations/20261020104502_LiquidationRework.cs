using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiquidationRework : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "final_period_charge",
                table: "contracts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // HĐ đang thanh lý trước khi có lựa chọn kỳ cuối: hóa đơn trả phòng đang tính theo "Tính kỳ lẻ" của khu ⇒ giữ nguyên cách tính đó,
            // nháp hóa đơn trả phòng tính lại không tự đổi sang thu trọn kỳ (M05 CT-BR-11).
            migrationBuilder.Sql("""
                UPDATE contracts c SET final_period_charge = 'Daily'
                FROM properties p
                WHERE p.id = c.property_id AND c.status = 'Liquidating' AND p.proration_mode = 'Daily';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "final_period_charge",
                table: "contracts");
        }
    }
}
