using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RentCycleTiersWriteOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "default_rent_cycle_months",
                table: "properties",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "payments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Receipt");

            migrationBuilder.AddColumn<decimal>(
                name: "written_off_amount",
                table: "invoices",
                type: "numeric(18,0)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "tiers",
                table: "fee_prices",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rent_cycle_months",
                table: "contracts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_properties_rent_cycle",
                table: "properties",
                sql: "default_rent_cycle_months IN (1, 2, 3, 6, 12)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_written_off",
                table: "invoices",
                sql: "written_off_amount >= 0 AND written_off_amount <= paid_amount");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_rent_cycle",
                table: "contracts",
                sql: "rent_cycle_months IN (1, 2, 3, 6, 12)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_properties_rent_cycle",
                table: "properties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_written_off",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_rent_cycle",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "default_rent_cycle_months",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "written_off_amount",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "tiers",
                table: "fee_prices");

            migrationBuilder.DropColumn(
                name: "rent_cycle_months",
                table: "contracts");
        }
    }
}
