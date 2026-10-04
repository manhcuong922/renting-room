using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_number_sequences",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prefix = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_number_sequences", x => new { x.organization_id, x.prefix, x.year });
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    billing_month = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    invoice_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    discount_total = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    issues = table.Column<string>(type: "jsonb", nullable: false),
                    snapshot_room_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    snapshot_contract_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    snapshot_representative_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    finalized_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.UniqueConstraint("ak_invoices_organization_contract_id", x => new { x.organization_id, x.contract_id, x.id });
                    table.UniqueConstraint("ak_invoices_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_invoices_number", "status = 'Draft' OR (invoice_no IS NOT NULL AND issue_date IS NOT NULL AND due_date IS NOT NULL)");
                    table.CheckConstraint("ck_invoices_paid", "paid_amount >= 0 AND paid_amount <= GREATEST(total_amount, 0)");
                    table.CheckConstraint("ck_invoices_period", "period_end >= period_start");
                    table.CheckConstraint("ck_invoices_total", "total_amount >= 0 OR status = 'Draft'");
                    table.CheckConstraint("ck_invoices_void_reason", "status <> 'Void' OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_invoices_contracts_organization_id_property_id_contract_id",
                        columns: x => new { x.organization_id, x.property_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_no = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_at = table.Column<DateOnly>(type: "date", nullable: false),
                    payer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reverse_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.UniqueConstraint("ak_payments_organization_contract_id", x => new { x.organization_id, x.contract_id, x.id });
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_reverse_reason", "status <> 'Reversed' OR reverse_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_payments_contracts_organization_id_property_id_contract_id",
                        columns: x => new { x.organization_id, x.property_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "property_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    service_from = table.Column<DateOnly>(type: "date", nullable: false),
                    service_to = table.Column<DateOnly>(type: "date", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    proration_factor = table.Column<decimal>(type: "numeric(12,8)", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    is_manually_edited = table.Column<bool>(type: "boolean", nullable: false),
                    system_quantity = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    system_unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    system_amount = table.Column<decimal>(type: "numeric(18,0)", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => x.id);
                    table.CheckConstraint("ck_invoice_lines_discount", "line_type <> 'ManualDiscount' OR amount <= 0");
                    table.CheckConstraint("ck_invoice_lines_note", "line_type NOT IN ('Surcharge','ManualDiscount') OR note IS NOT NULL");
                    table.CheckConstraint("ck_invoice_lines_sign", "line_type = 'ManualDiscount' OR amount >= 0");
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_meter_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    meter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_reading_id = table.Column<Guid>(type: "uuid", nullable: false),
                    end_reading_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    end_value = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    consumption = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    voided = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_meter_segments", x => x.id);
                    table.CheckConstraint("ck_invoice_meter_segments_values", "end_value >= start_value AND consumption = end_value - start_value");
                    table.ForeignKey(
                        name: "fk_invoice_meter_segments_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invoice_meter_segments_meters_organization_id_meter_id",
                        columns: x => new { x.organization_id, x.meter_id },
                        principalTable: "meters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_allocations_invoices_organization_id_contract_id_in",
                        columns: x => new { x.organization_id, x.contract_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "contract_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_organization_id_contract_id_pa",
                        columns: x => new { x.organization_id, x.contract_id, x.payment_id },
                        principalTable: "payments",
                        principalColumns: new[] { "organization_id", "contract_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_fee_type",
                table: "invoice_lines",
                columns: new[] { "organization_id", "fee_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_organization_id_invoice_id",
                table: "invoice_lines",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_fee",
                table: "invoice_lines",
                columns: new[] { "invoice_id", "fee_type_id" },
                unique: true,
                filter: "fee_type_id IS NOT NULL AND is_system");

            migrationBuilder.CreateIndex(
                name: "ux_invoice_lines_rent",
                table: "invoice_lines",
                column: "invoice_id",
                unique: true,
                filter: "line_type = 'Rent' AND is_system");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_meter_segments_organization_id_invoice_id",
                table: "invoice_meter_segments",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_meter_segments_organization_id_meter_id",
                table: "invoice_meter_segments",
                columns: new[] { "organization_id", "meter_id" });

            migrationBuilder.CreateIndex(
                name: "ux_invoice_meter_segments_end",
                table: "invoice_meter_segments",
                column: "end_reading_id",
                unique: true,
                filter: "NOT voided");

            migrationBuilder.CreateIndex(
                name: "ux_invoice_meter_segments_start",
                table: "invoice_meter_segments",
                column: "start_reading_id",
                unique: true,
                filter: "NOT voided");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_organization_id_property_id_contract_id",
                table: "invoices",
                columns: new[] { "organization_id", "property_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_property_month",
                table: "invoices",
                columns: new[] { "organization_id", "property_id", "billing_month" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_room",
                table: "invoices",
                columns: new[] { "organization_id", "room_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ux_invoices_invoice_no",
                table: "invoices",
                columns: new[] { "organization_id", "invoice_no" },
                unique: true,
                filter: "invoice_no IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_period",
                table: "invoices",
                columns: new[] { "contract_id", "invoice_type", "period_start" },
                unique: true,
                filter: "status <> 'Void'");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_invoice",
                table: "payment_allocations",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_organization_id_contract_id_invoice_id",
                table: "payment_allocations",
                columns: new[] { "organization_id", "contract_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_organization_id_contract_id_payment_id",
                table: "payment_allocations",
                columns: new[] { "organization_id", "contract_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_contract",
                table: "payments",
                columns: new[] { "organization_id", "contract_id", "paid_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_organization_id_property_id_contract_id",
                table: "payments",
                columns: new[] { "organization_id", "property_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payments_receipt_no",
                table: "payments",
                columns: new[] { "organization_id", "receipt_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_number_sequences");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "invoice_meter_segments");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}
