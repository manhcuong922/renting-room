using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertiesRentersContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phòng scaffold cũ chưa thuộc khu trọ nào ⇒ không chuyển được sang mô hình mới (chỉ có dữ liệu dev).
            migrationBuilder.Sql("DELETE FROM rooms;");

            migrationBuilder.DropForeignKey(
                name: "fk_rooms_organizations_organization_id",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "ix_rooms_organization_id",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "monthly_rent",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "name",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "status",
                table: "rooms");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "password_changed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "removed_by",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "temp_password_expires_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "amenities",
                table: "rooms",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "archived_at",
                table: "rooms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "area_m2",
                table: "rooms",
                type: "numeric(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "rooms",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "default_deposit",
                table: "rooms",
                type: "numeric(18,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "rooms",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "floor",
                table: "rooms",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_under_maintenance",
                table: "rooms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "listed_rent",
                table: "rooms",
                type: "numeric(18,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "maintenance_note",
                table: "rooms",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_occupants",
                table: "rooms",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "property_id",
                table: "rooms",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "organizations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_managers",
                table: "organizations",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_rooms_organization_property_id",
                table: "rooms",
                columns: new[] { "organization_id", "property_id", "id" });

            migrationBuilder.CreateTable(
                name: "contract_number_sequences",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_number_sequences", x => new { x.organization_id, x.year });
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    street_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    commune_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    province_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    commune_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    province_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evn_customer_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    default_billing_anchor_day = table.Column<int>(type: "integer", nullable: false),
                    default_charge_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    default_payment_due_days = table.Column<int>(type: "integer", nullable: false),
                    default_proration_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    default_notice_days = table.Column<int>(type: "integer", nullable: false),
                    lessor_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    lessor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lessor_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    lessor_phone = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    lessor_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    lessor_id_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    lessor_id_number_encrypted = table.Column<byte[]>(type: "bytea", nullable: true),
                    lessor_id_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    lessor_id_issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    lessor_id_issue_place = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lessor_date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    lessor_tax_code = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    lessor_representative_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lessor_representative_title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    authorization_doc_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    authorization_doc_date = table.Column<DateOnly>(type: "date", nullable: true),
                    land_parcel_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    land_map_sheet_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ownership_certificate_no = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_account_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    bank_account_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    house_rules_text = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
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
                    table.PrimaryKey("pk_properties", x => x.id);
                    table.UniqueConstraint("ak_properties_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_properties_anchor_day", "default_billing_anchor_day BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_properties_lessor_organization", "lessor_type IS DISTINCT FROM 'Organization' OR (lessor_tax_code IS NOT NULL AND lessor_representative_name IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_properties_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "renters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    full_name_search = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    gender = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    nationality = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    id_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    id_number_encrypted = table.Column<byte[]>(type: "bytea", nullable: false),
                    id_number_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    id_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    id_issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    id_issue_place = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    permanent_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occupation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    workplace = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    emergency_contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    emergency_contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("pk_renters", x => x.id);
                    table.UniqueConstraint("ak_renters_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_renters_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_groups", x => x.id);
                    table.UniqueConstraint("ak_room_groups_organization_property_id", x => new { x.organization_id, x.property_id, x.id });
                    table.ForeignKey(
                        name: "fk_room_groups_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    representative_renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    signed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    signed_place = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notice_given_date = table.Column<DateOnly>(type: "date", nullable: true),
                    planned_move_out_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notice_days = table.Column<int>(type: "integer", nullable: false),
                    deposit_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    deposit_terms = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    billing_anchor_day = table.Column<int>(type: "integer", nullable: false),
                    charge_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    proration_mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payment_due_days = table.Column<int>(type: "integer", nullable: false),
                    payment_methods = table.Column<string[]>(type: "text[]", nullable: false),
                    copies_count = table.Column<int>(type: "integer", nullable: false),
                    terms_text = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    previous_contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lessor_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    house_rules_snapshot = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    utility_price_snapshot = table.Column<string>(type: "jsonb", nullable: true),
                    termination_reason = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    termination_ground = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    termination_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                    table.UniqueConstraint("ak_contracts_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_contracts_activated_snapshot", "status NOT IN ('Active','Liquidating','Ended') OR (effective_date IS NOT NULL AND lessor_snapshot IS NOT NULL)");
                    table.CheckConstraint("ck_contracts_actual_end", "actual_end_date IS NULL OR actual_end_date >= start_date");
                    table.CheckConstraint("ck_contracts_effective_date", "effective_date IS NULL OR signed_date IS NULL OR effective_date >= signed_date");
                    table.CheckConstraint("ck_contracts_end_after_start", "end_date IS NULL OR end_date > start_date");
                    table.CheckConstraint("ck_contracts_liquidation_end", "status NOT IN ('Liquidating','Ended') OR actual_end_date IS NOT NULL");
                    table.CheckConstraint("ck_contracts_settings", "billing_anchor_day BETWEEN 1 AND 31 AND deposit_amount >= 0 AND copies_count BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_contracts_termination_reason", "status <> 'Ended' OR termination_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_contracts_renters_organization_id_representative_renter_id",
                        columns: x => new { x.organization_id, x.representative_renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_rooms_organization_id_property_id_room_id",
                        columns: x => new { x.organization_id, x.property_id, x.room_id },
                        principalTable: "rooms",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_room_group_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_room_group_members_room_groups_organization_id_property_id_",
                        columns: x => new { x.organization_id, x.property_id, x.room_group_id },
                        principalTable: "room_groups",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_room_group_members_rooms_organization_id_property_id_room_id",
                        columns: x => new { x.organization_id, x.property_id, x.room_id },
                        principalTable: "rooms",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    condition_at_handover = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    condition_at_return = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    value_estimate = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    compensation_value = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_assets", x => x.id);
                    table.CheckConstraint("ck_contract_assets_money", "(value_estimate IS NULL OR value_estimate >= 0) AND (compensation_value IS NULL OR compensation_value >= 0)");
                    table.CheckConstraint("ck_contract_assets_quantity", "quantity BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "fk_contract_assets_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_occupants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    move_in_date = table.Column<DateOnly>(type: "date", nullable: false),
                    move_out_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expected_end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_occupants", x => x.id);
                    table.CheckConstraint("ck_contract_occupants_dates", "move_out_date IS NULL OR move_out_date >= move_in_date");
                    table.ForeignKey(
                        name: "fk_contract_occupants_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_occupants_renters_organization_id_renter_id",
                        columns: x => new { x.organization_id, x.renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_rent_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    monthly_rent = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    addendum_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_rent_terms", x => x.id);
                    table.CheckConstraint("ck_contract_rent_terms_positive", "monthly_rent > 0");
                    table.ForeignKey(
                        name: "fk_contract_rent_terms_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contract_vehicles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehicle_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    plate_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    brand_color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    registered_from = table.Column<DateOnly>(type: "date", nullable: false),
                    registered_to = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_vehicles", x => x.id);
                    table.CheckConstraint("ck_contract_vehicles_dates", "registered_to IS NULL OR registered_to >= registered_from");
                    table.ForeignKey(
                        name: "fk_contract_vehicles_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_removed",
                table: "users",
                sql: "(status = 'Removed') = (removed_at IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ux_rooms_code",
                table: "rooms",
                columns: new[] { "property_id", "code" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_area",
                table: "rooms",
                sql: "area_m2 IS NULL OR area_m2 > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_maintenance_not_archived",
                table: "rooms",
                sql: "NOT (is_under_maintenance AND archived_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_max_occupants",
                table: "rooms",
                sql: "max_occupants BETWEEN 1 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_money",
                table: "rooms",
                sql: "(listed_rent IS NULL OR listed_rent >= 0) AND (default_deposit IS NULL OR default_deposit >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_max_managers",
                table: "organizations",
                sql: "max_managers BETWEEN 0 AND 100");

            migrationBuilder.CreateIndex(
                name: "ix_contract_assets_organization_id_contract_id",
                table: "contract_assets",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_occupants_organization_id_contract_id",
                table: "contract_occupants",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_occupants_renter",
                table: "contract_occupants",
                columns: new[] { "organization_id", "renter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_rent_terms_organization_id_contract_id",
                table: "contract_rent_terms",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_rent_terms_effective_from",
                table: "contract_rent_terms",
                columns: new[] { "contract_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contract_vehicles_organization_id_contract_id",
                table: "contract_vehicles",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_vehicles_active_plate",
                table: "contract_vehicles",
                columns: new[] { "organization_id", "plate_number" },
                unique: true,
                filter: "registered_to IS NULL AND plate_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_id_property_id_room_id",
                table: "contracts",
                columns: new[] { "organization_id", "property_id", "room_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_property_status",
                table: "contracts",
                columns: new[] { "organization_id", "property_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_representative",
                table: "contracts",
                columns: new[] { "organization_id", "representative_renter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_room_id",
                table: "contracts",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "ux_contracts_contract_no",
                table: "contracts",
                columns: new[] { "organization_id", "contract_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_properties_code",
                table: "properties",
                columns: new[] { "organization_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_renters_phone",
                table: "renters",
                columns: new[] { "organization_id", "phone" });

            migrationBuilder.CreateIndex(
                name: "ux_renters_id_number",
                table: "renters",
                columns: new[] { "organization_id", "id_number_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_group_members_organization_id_property_id_room_group_id",
                table: "room_group_members",
                columns: new[] { "organization_id", "property_id", "room_group_id" });

            migrationBuilder.CreateIndex(
                name: "ix_room_group_members_organization_id_property_id_room_id",
                table: "room_group_members",
                columns: new[] { "organization_id", "property_id", "room_id" });

            migrationBuilder.CreateIndex(
                name: "ux_room_group_members_group_room",
                table: "room_group_members",
                columns: new[] { "room_group_id", "room_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_groups_property_name",
                table: "room_groups",
                columns: new[] { "property_id", "name" });

            migrationBuilder.AddForeignKey(
                name: "fk_rooms_properties_organization_id_property_id",
                table: "rooms",
                columns: new[] { "organization_id", "property_id" },
                principalTable: "properties",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);

            // CT-BR-01: một phòng không có 2 hợp đồng chồng thời gian (Active/Liquidating/Ended).
            // daterange(start, NULL) = vô hạn ⇒ hợp đồng chưa thanh lý chiếm phòng tới vô hạn.
            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT ex_contracts_room_period
                EXCLUDE USING gist (room_id WITH =, daterange(start_date, actual_end_date, '[]') WITH &&)
                WHERE (status IN ('Active', 'Liquidating', 'Ended'));
                """);

            // CT-BR-08: một người không có 2 khoảng ở chồng lấn trong cùng hợp đồng.
            migrationBuilder.Sql("""
                ALTER TABLE contract_occupants ADD CONSTRAINT ex_contract_occupants_period
                EXCLUDE USING gist (contract_id WITH =, renter_id WITH =, daterange(move_in_date, move_out_date, '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE contract_occupants DROP CONSTRAINT IF EXISTS ex_contract_occupants_period;");
            migrationBuilder.Sql("ALTER TABLE contracts DROP CONSTRAINT IF EXISTS ex_contracts_room_period;");

            migrationBuilder.DropForeignKey(
                name: "fk_rooms_properties_organization_id_property_id",
                table: "rooms");

            migrationBuilder.DropTable(
                name: "contract_assets");

            migrationBuilder.DropTable(
                name: "contract_number_sequences");

            migrationBuilder.DropTable(
                name: "contract_occupants");

            migrationBuilder.DropTable(
                name: "contract_rent_terms");

            migrationBuilder.DropTable(
                name: "contract_vehicles");

            migrationBuilder.DropTable(
                name: "room_group_members");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropTable(
                name: "room_groups");

            migrationBuilder.DropTable(
                name: "renters");

            migrationBuilder.DropTable(
                name: "properties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_removed",
                table: "users");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_rooms_organization_property_id",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "ux_rooms_code",
                table: "rooms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_area",
                table: "rooms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_maintenance_not_archived",
                table: "rooms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_max_occupants",
                table: "rooms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_money",
                table: "rooms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_max_managers",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "password_changed_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "removed_by",
                table: "users");

            migrationBuilder.DropColumn(
                name: "temp_password_expires_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "amenities",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "archived_at",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "area_m2",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "code",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "default_deposit",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "description",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "floor",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "is_under_maintenance",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "listed_rent",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "maintenance_note",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "max_occupants",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "property_id",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "address",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "max_managers",
                table: "organizations");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_rent",
                table: "rooms",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "rooms",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "rooms",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_organization_id",
                table: "rooms",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_rooms_organizations_organization_id",
                table: "rooms",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
