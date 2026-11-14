using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManagerPropertyScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "renter_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_renter_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_renter_claims_renters_organization_id_renter_id",
                        columns: x => new { x.organization_id, x.renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_renter_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_property_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_property_access", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_property_access_properties_organization_id_property_id",
                        columns: x => new { x.organization_id, x.property_id },
                        principalTable: "properties",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_property_access_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_renter_claims_organization_id_renter_id",
                table: "renter_claims",
                columns: new[] { "organization_id", "renter_id" });

            migrationBuilder.CreateIndex(
                name: "ix_renter_claims_user_id",
                table: "renter_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_renter_claims_renter_user",
                table: "renter_claims",
                columns: new[] { "renter_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_property_access_property",
                table: "user_property_access",
                columns: new[] { "organization_id", "property_id" });

            migrationBuilder.CreateIndex(
                name: "ux_user_property_access_user_property",
                table: "user_property_access",
                columns: new[] { "user_id", "property_id" },
                unique: true);

            // M01 ID-BR-29 (08/11/2026): phó quản lý đang có ⇒ được giao mọi khu hiện có của tổ chức (không ai mất quyền đột ngột).
            migrationBuilder.Sql("""
                INSERT INTO user_property_access (id, organization_id, user_id, property_id, created_at)
                SELECT gen_random_uuid(), u.organization_id, u.id, p.id, now()
                FROM users u
                JOIN properties p ON p.organization_id = u.organization_id
                WHERE u.role = 'OrgManager' AND u.status <> 'Removed'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "renter_claims");

            migrationBuilder.DropTable(
                name: "user_property_access");
        }
    }
}
