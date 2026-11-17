using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "zalo_chat_id",
                table: "renters",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "zalo_linked_at",
                table: "renters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_finalize",
                table: "properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short[]>(
                name: "overdue_reminder_days",
                table: "properties",
                type: "smallint[]",
                nullable: false,
                defaultValue: new short[0]);

            migrationBuilder.CreateTable(
                name: "notification_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    step = table.Column<short>(type: "smallint", nullable: false),
                    recipient = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    attempts = table.Column<short>(type: "smallint", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_outbox", x => x.id);
                    table.UniqueConstraint("ak_notification_outbox_organization_id_id", x => new { x.organization_id, x.id });
                    table.CheckConstraint("ck_notification_outbox_attempts", "attempts BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_notification_outbox_channel", "channel IN ('Zalo','Email')");
                    table.CheckConstraint("ck_notification_outbox_kind", "kind IN ('InvoiceIssued','OverdueReminder','ManualResend')");
                    table.CheckConstraint("ck_notification_outbox_status", "status IN ('Pending','Sending','Sent','Failed','Skipped','Cancelled')");
                    table.ForeignKey(
                        name: "fk_notification_outbox_invoices_organization_id_invoice_id",
                        columns: x => new { x.organization_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_outbox_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notification_outbox_renters_organization_id_renter_id",
                        columns: x => new { x.organization_id, x.renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "zalo_link_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zalo_link_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_zalo_link_codes_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_zalo_link_codes_renters_organization_id_renter_id",
                        columns: x => new { x.organization_id, x.renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_renters_zalo_chat",
                table: "renters",
                column: "zalo_chat_id",
                filter: "zalo_chat_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_properties_overdue_reminder_days",
                table: "properties",
                sql: "cardinality(overdue_reminder_days) <= 3");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_invoice",
                table: "notification_outbox",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_pending",
                table: "notification_outbox",
                column: "next_attempt_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_renter",
                table: "notification_outbox",
                columns: new[] { "organization_id", "renter_id" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_outbox_dedupe",
                table: "notification_outbox",
                columns: new[] { "invoice_id", "channel", "kind", "step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zalo_link_codes_renter",
                table: "zalo_link_codes",
                columns: new[] { "organization_id", "renter_id" });

            migrationBuilder.CreateIndex(
                name: "ux_zalo_link_codes_hash",
                table: "zalo_link_codes",
                column: "code_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_outbox");

            migrationBuilder.DropTable(
                name: "zalo_link_codes");

            migrationBuilder.DropIndex(
                name: "ix_renters_zalo_chat",
                table: "renters");

            migrationBuilder.DropCheckConstraint(
                name: "ck_properties_overdue_reminder_days",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "zalo_chat_id",
                table: "renters");

            migrationBuilder.DropColumn(
                name: "zalo_linked_at",
                table: "renters");

            migrationBuilder.DropColumn(
                name: "notify_on_finalize",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "overdue_reminder_days",
                table: "properties");
        }
    }
}
