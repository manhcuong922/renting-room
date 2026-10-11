using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "representative_since",
                table: "contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "contract_occupants",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Resident");

            migrationBuilder.CreateTable(
                name: "contract_representatives",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    renter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_representatives", x => x.id);
                    table.CheckConstraint("ck_contract_representatives_dates", "to_date >= from_date");
                    table.ForeignKey(
                        name: "fk_contract_representatives_contracts_organization_id_contract",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_representatives_renters_organization_id_renter_id",
                        columns: x => new { x.organization_id, x.renter_id },
                        principalTable: "renters",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_representatives_organization_id_contract_id",
                table: "contract_representatives",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_representatives_renter",
                table: "contract_representatives",
                columns: new[] { "organization_id", "renter_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_representatives");

            migrationBuilder.DropColumn(
                name: "representative_since",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "role",
                table: "contract_occupants");
        }
    }
}
