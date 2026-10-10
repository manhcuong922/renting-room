using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompensationAsSurcharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CT-BR-23 (10/10/2026): bồi thường = phụ thu nhập tay — dòng "Bồi thường tài sản" do hệ thống sinh trước đây giữ số tiền, thành phụ thu.
            migrationBuilder.Sql("""
                UPDATE invoice_lines SET line_type = 'Surcharge', is_system = FALSE, note = COALESCE(note, 'Bồi thường tài sản khi trả phòng')
                WHERE line_type = 'Compensation';
                """);

            migrationBuilder.DropColumn(
                name: "compensation_value",
                table: "contract_assets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "compensation_value",
                table: "contract_assets",
                type: "numeric(18,0)",
                nullable: true);
        }
    }
}
