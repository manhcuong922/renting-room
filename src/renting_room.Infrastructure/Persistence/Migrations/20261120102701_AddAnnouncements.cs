using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_outbox_kind",
                table: "notification_outbox");

            migrationBuilder.AlterColumn<Guid>(
                name: "invoice_id",
                table: "notification_outbox",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "announcement_id",
                table: "notification_outbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    body = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: false),
                    all_rooms = table.Column<bool>(type: "boolean", nullable: false),
                    room_count = table.Column<int>(type: "integer", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                    table.UniqueConstraint("ak_announcements_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_announcements_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_announcements_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_outbox_organization_id_announcement_id",
                table: "notification_outbox",
                columns: new[] { "organization_id", "announcement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_notification_outbox_announcement",
                table: "notification_outbox",
                columns: new[] { "announcement_id", "renter_id", "channel" },
                unique: true,
                filter: "announcement_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_outbox_kind",
                table: "notification_outbox",
                sql: "kind IN ('InvoiceIssued','OverdueReminder','ManualResend','Announcement')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_outbox_source",
                table: "notification_outbox",
                sql: "num_nonnulls(invoice_id, announcement_id) = 1");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_property_created",
                table: "announcements",
                columns: new[] { "organization_id", "property_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_notification_outbox_announcements_organization_id_announcem",
                table: "notification_outbox",
                columns: new[] { "organization_id", "announcement_id" },
                principalTable: "announcements",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_notification_outbox_announcements_organization_id_announcem",
                table: "notification_outbox");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropIndex(
                name: "ix_notification_outbox_organization_id_announcement_id",
                table: "notification_outbox");

            migrationBuilder.DropIndex(
                name: "ux_notification_outbox_announcement",
                table: "notification_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_outbox_kind",
                table: "notification_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notification_outbox_source",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "announcement_id",
                table: "notification_outbox");

            migrationBuilder.AlterColumn<Guid>(
                name: "invoice_id",
                table: "notification_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_notification_outbox_kind",
                table: "notification_outbox",
                sql: "kind IN ('InvoiceIssued','OverdueReminder','ManualResend')");
        }
    }
}
