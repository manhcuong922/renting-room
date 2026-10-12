using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenterConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renter_consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    given_at = table.Column<DateOnly>(type: "date", nullable: false),
                    withdrawn_at = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_renter_consents", x => x.id);
                    table.CheckConstraint("ck_renter_consents_dates", "withdrawn_at IS NULL OR withdrawn_at >= given_at");
                    table.ForeignKey(
                        name: "fk_renter_consents_renters_organization_id_renter_id",
                        columns: x => new { x.organization_id, x.renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_renter_consents_renter",
                table: "renter_consents",
                columns: new[] { "organization_id", "renter_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "renter_consents");
        }
    }
}
