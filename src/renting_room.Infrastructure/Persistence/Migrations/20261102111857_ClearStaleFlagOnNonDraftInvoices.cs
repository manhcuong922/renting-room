using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClearStaleFlagOnNonDraftInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M07 BL-BR-20 (L-02): trước 02/11/2026 chốt không xóa cờ "Cần tính lại" ⇒ dọn cờ còn sót trên phiếu đã chốt / đã hủy.
            migrationBuilder.Sql("UPDATE invoices SET is_stale = FALSE WHERE status <> 'Draft' AND is_stale;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
