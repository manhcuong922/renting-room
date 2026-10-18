using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameSigningSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "lessor_snapshot",
                table: "contracts",
                newName: "signing_snapshot");

            // Chuyển snapshot cấu trúc cũ (chỉ bên cho thuê, phẳng) sang cấu trúc mới; bên thuê/phòng của HĐ cũ để null.
            migrationBuilder.Sql("""
                UPDATE contracts
                SET signing_snapshot = jsonb_build_object(
                    'lessor', signing_snapshot - 'bankAccount' - 'propertyName' - 'propertyAddress' - 'capturedAt',
                    'representative', NULL,
                    'room', NULL,
                    'bankAccount', signing_snapshot -> 'bankAccount',
                    'propertyName', signing_snapshot ->> 'propertyName',
                    'propertyAddress', signing_snapshot ->> 'propertyAddress',
                    'capturedAt', signing_snapshot ->> 'capturedAt')
                WHERE signing_snapshot ? 'name';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts",
                sql: "status NOT IN ('Active','Liquidating','Ended') OR (effective_date IS NOT NULL AND signing_snapshot IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "signing_snapshot",
                table: "contracts",
                newName: "lessor_snapshot");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts",
                sql: "status NOT IN ('Active','Liquidating','Ended') OR (effective_date IS NOT NULL AND lessor_snapshot IS NOT NULL)");
        }
    }
}
