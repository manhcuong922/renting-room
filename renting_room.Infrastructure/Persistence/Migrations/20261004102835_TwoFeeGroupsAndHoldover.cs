using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TwoFeeGroupsAndHoldover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_types_default_quantity",
                table: "fee_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_types_fixed_basis",
                table: "fee_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_types_vehicle_type",
                table: "fee_types");

            migrationBuilder.RenameColumn(
                name: "fixed_basis",
                table: "fee_types",
                newName: "charge_basis");

            // FE-06: 3 nhóm → 2 nhóm. Fixed → Service (giữ cách tính), Quantity → Service/PerUnit; chuyển cả bản chụp giá lúc ký.
            migrationBuilder.Sql("""
                UPDATE fee_types SET fee_group = 'Service' WHERE fee_group = 'Fixed';
                UPDATE fee_types SET fee_group = 'Service', charge_basis = 'PerUnit' WHERE fee_group = 'Quantity';
                UPDATE contracts SET utility_price_snapshot = (
                    SELECT jsonb_agg((e - 'fixedBasis') || jsonb_build_object(
                        'group', CASE WHEN e->>'group' = 'Metered' THEN 'Metered' ELSE 'Service' END,
                        'chargeBasis', CASE WHEN e->>'group' = 'Quantity' THEN to_jsonb('PerUnit'::text)
                                            ELSE COALESCE(e->'fixedBasis', 'null'::jsonb) END))
                    FROM jsonb_array_elements(utility_price_snapshot) e)
                WHERE jsonb_typeof(utility_price_snapshot) = 'array' AND jsonb_array_length(utility_price_snapshot) > 0;
                """);

            migrationBuilder.AddColumn<string>(
                name: "holdover_note",
                table: "contracts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "holdover_since",
                table: "contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_types_charge_basis",
                table: "fee_types",
                sql: "(fee_group = 'Service') = (charge_basis IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_types_default_quantity",
                table: "fee_types",
                sql: "charge_basis = 'PerUnit' OR default_quantity IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_types_vehicle_type",
                table: "fee_types",
                sql: "vehicle_type IS NULL OR charge_basis = 'PerUnit'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_types_charge_basis",
                table: "fee_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_types_default_quantity",
                table: "fee_types");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fee_types_vehicle_type",
                table: "fee_types");

            migrationBuilder.DropColumn(
                name: "holdover_note",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "holdover_since",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "charge_basis",
                table: "fee_types",
                newName: "fixed_basis");

            migrationBuilder.Sql("""
                UPDATE fee_types SET fee_group = 'Quantity', fixed_basis = NULL WHERE fee_group = 'Service' AND fixed_basis = 'PerUnit';
                UPDATE fee_types SET fee_group = 'Fixed' WHERE fee_group = 'Service';
                UPDATE contracts SET utility_price_snapshot = (
                    SELECT jsonb_agg((e - 'chargeBasis') || jsonb_build_object(
                        'group', CASE WHEN e->>'group' = 'Metered' THEN 'Metered'
                                      WHEN e->>'chargeBasis' = 'PerUnit' THEN 'Quantity' ELSE 'Fixed' END,
                        'fixedBasis', CASE WHEN e->>'chargeBasis' IN ('PerRoom', 'PerOccupant') THEN e->'chargeBasis' ELSE 'null'::jsonb END))
                    FROM jsonb_array_elements(utility_price_snapshot) e)
                WHERE jsonb_typeof(utility_price_snapshot) = 'array' AND jsonb_array_length(utility_price_snapshot) > 0;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_types_default_quantity",
                table: "fee_types",
                sql: "fee_group = 'Quantity' OR default_quantity IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_types_fixed_basis",
                table: "fee_types",
                sql: "(fee_group = 'Fixed') = (fixed_basis IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fee_types_vehicle_type",
                table: "fee_types",
                sql: "vehicle_type IS NULL OR fee_group = 'Quantity'");
        }
    }
}
