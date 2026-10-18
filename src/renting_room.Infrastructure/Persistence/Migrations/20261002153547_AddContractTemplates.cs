using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "clauses",
                table: "contracts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_type",
                table: "contracts",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "RoomRental");

            migrationBuilder.AddColumn<string>(
                name: "custom_field_definitions",
                table: "contracts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_field_values",
                table: "contracts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "template_id",
                table: "contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "contracts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "contract_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    contract_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    clauses = table.Column<string>(type: "jsonb", nullable: false),
                    field_definitions = table.Column<string>(type: "jsonb", nullable: false),
                    no_deposit = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_contract_templates", x => x.id);
                    table.UniqueConstraint("ak_contract_templates_organization_id_id", x => new { x.organization_id, x.id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_id_template_id",
                table: "contracts",
                columns: new[] { "organization_id", "template_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_templates_name",
                table: "contract_templates",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_contracts_contract_templates_organization_id_template_id",
                table: "contracts",
                columns: new[] { "organization_id", "template_id" },
                principalTable: "contract_templates",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contracts_contract_templates_organization_id_template_id",
                table: "contracts");

            migrationBuilder.DropTable(
                name: "contract_templates");

            migrationBuilder.DropIndex(
                name: "ix_contracts_organization_id_template_id",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "clauses",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "contract_type",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "custom_field_definitions",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "custom_field_values",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "template_id",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "title",
                table: "contracts");
        }
    }
}
