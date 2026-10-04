using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    installed_date = table.Column<DateOnly>(type: "date", nullable: false),
                    removed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    replaced_by_meter_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_meters", x => x.id);
                    table.UniqueConstraint("ak_meters_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_meters_dates", "removed_date IS NULL OR removed_date >= installed_date");
                    table.ForeignKey(
                        name: "fk_meters_fee_types_organization_id_property_id_fee_type_id",
                        columns: x => new { x.organization_id, x.property_id, x.fee_type_id },
                        principalTable: "fee_types",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meters_rooms_organization_id_property_id_room_id",
                        columns: x => new { x.organization_id, x.property_id, x.room_id },
                        principalTable: "rooms",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "meter_readings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    reading_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closing_period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meter_readings", x => x.id);
                    table.CheckConstraint("ck_meter_readings_contract", "kind NOT IN ('Handover','Periodic','Final') OR contract_id IS NOT NULL");
                    table.CheckConstraint("ck_meter_readings_period", "(kind = 'Periodic') = (closing_period_start IS NOT NULL)");
                    table.CheckConstraint("ck_meter_readings_value", "value >= 0");
                    table.ForeignKey(
                        name: "fk_meter_readings_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meter_readings_meters_organization_id_meter_id",
                        columns: x => new { x.organization_id, x.meter_id },
                        principalTable: "meters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meter_readings_order",
                table: "meter_readings",
                columns: new[] { "meter_id", "reading_date", "sequence" },
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meter_readings_organization_id_contract_id",
                table: "meter_readings",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meter_readings_organization_id_meter_id",
                table: "meter_readings",
                columns: new[] { "organization_id", "meter_id" });

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

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_initial",
                table: "meter_readings",
                column: "meter_id",
                unique: true,
                filter: "kind = 'Initial' AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_periodic",
                table: "meter_readings",
                columns: new[] { "meter_id", "contract_id", "closing_period_start" },
                unique: true,
                filter: "kind = 'Periodic' AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_meter_readings_removal",
                table: "meter_readings",
                column: "meter_id",
                unique: true,
                filter: "kind = 'Removal' AND voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_meters_fee_type",
                table: "meters",
                columns: new[] { "organization_id", "fee_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meters_organization_id_property_id_fee_type_id",
                table: "meters",
                columns: new[] { "organization_id", "property_id", "fee_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meters_organization_id_property_id_room_id",
                table: "meters",
                columns: new[] { "organization_id", "property_id", "room_id" });

            migrationBuilder.CreateIndex(
                name: "ux_meters_active",
                table: "meters",
                columns: new[] { "room_id", "fee_type_id" },
                unique: true,
                filter: "removed_date IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meter_readings");

            migrationBuilder.DropTable(
                name: "meters");
        }
    }
}
