using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeCatalogAndSensitiveAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_view_sensitive_data",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "liquidation_started_on",
                table: "contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_contracts_organization_property_id",
                table: "contracts",
                columns: new[] { "organization_id", "property_id", "id" });

            migrationBuilder.CreateTable(
                name: "fee_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_normalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fee_group = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    fixed_basis = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    system_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    auto_attach = table.Column<bool>(type: "boolean", nullable: false),
                    default_quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_types", x => x.id);
                    table.UniqueConstraint("ak_fee_types_organization_id_id", x => new { x.organization_id, x.id });
                    table.UniqueConstraint("ak_fee_types_organization_property_id", x => new { x.organization_id, x.property_id, x.id });
                    table.CheckConstraint("ck_fee_types_default_quantity", "fee_group = 'Quantity' OR default_quantity IS NULL");
                    table.CheckConstraint("ck_fee_types_fixed_basis", "(fee_group = 'Fixed') = (fixed_basis IS NOT NULL)");
                    table.CheckConstraint("ck_fee_types_system_code", "system_code IS NULL OR fee_group = 'Metered'");
                    table.CheckConstraint("ck_fee_types_vehicle_type", "vehicle_type IS NULL OR fee_group = 'Quantity'");
                    table.ForeignKey(
                        name: "fk_fee_types_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_fees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    unit_price_override = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_fees", x => x.id);
                    table.CheckConstraint("ck_contract_fees_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_contract_fees_values", "quantity > 0 AND (unit_price_override IS NULL OR unit_price_override >= 0)");
                    table.ForeignKey(
                        name: "fk_contract_fees_contracts_organization_id_property_id_contrac",
                        columns: x => new { x.organization_id, x.property_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_fees_fee_types_organization_id_property_id_fee_typ",
                        columns: x => new { x.organization_id, x.property_id, x.fee_type_id },
                        principalTable: "fee_types",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_prices", x => x.id);
                    table.CheckConstraint("ck_fee_prices_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_fee_prices_fee_types_organization_id_fee_type_id",
                        columns: x => new { x.organization_id, x.fee_type_id },
                        principalTable: "fee_types",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_fees_fee_type",
                table: "contract_fees",
                columns: new[] { "organization_id", "fee_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_fees_organization_id_property_id_contract_id",
                table: "contract_fees",
                columns: new[] { "organization_id", "property_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_fees_organization_id_property_id_fee_type_id",
                table: "contract_fees",
                columns: new[] { "organization_id", "property_id", "fee_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fee_prices_organization_id_fee_type_id",
                table: "fee_prices",
                columns: new[] { "organization_id", "fee_type_id" });

            migrationBuilder.CreateIndex(
                name: "ux_fee_prices_effective_from",
                table: "fee_prices",
                columns: new[] { "fee_type_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fee_types_name",
                table: "fee_types",
                columns: new[] { "property_id", "name_normalized" },
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_fee_types_system_code",
                table: "fee_types",
                columns: new[] { "property_id", "system_code" },
                unique: true,
                filter: "archived_at IS NULL AND system_code IS NOT NULL");

            // CT-BR-19: một khoản thu không có 2 dòng chồng thời gian trong cùng HĐ.
            migrationBuilder.Sql("""
                ALTER TABLE contract_fees ADD CONSTRAINT ex_contract_fees_period
                EXCLUDE USING gist (contract_id WITH =, fee_type_id WITH =, daterange(effective_from, effective_to, '[]') WITH &&);
                """);

            // FE-UC-01 cho các khu đã có: Điện + Nước theo công tơ, chưa có giá — đi theo công tơ của phòng, không gắn vào HĐ (FE-BR-17).
            migrationBuilder.Sql("""
                INSERT INTO fee_types (id, organization_id, property_id, name, name_normalized, fee_group, unit, system_code,
                                       auto_attach, sort_order, created_at)
                SELECT gen_random_uuid(), p.organization_id, p.id, d.name, d.name_normalized, 'Metered', d.unit, d.code, false, d.sort_order, now()
                FROM properties p
                CROSS JOIN (VALUES ('Điện', 'điện', 'kWh', 'ELECTRICITY', 0), ('Nước', 'nước', 'm³', 'WATER', 1))
                    AS d(name, name_normalized, unit, code, sort_order);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE contract_fees DROP CONSTRAINT IF EXISTS ex_contract_fees_period;");

            migrationBuilder.DropTable(
                name: "contract_fees");

            migrationBuilder.DropTable(
                name: "fee_prices");

            migrationBuilder.DropTable(
                name: "fee_types");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_contracts_organization_property_id",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "can_view_sensitive_data",
                table: "users");

            migrationBuilder.DropColumn(
                name: "liquidation_started_on",
                table: "contracts");
        }
    }
}
