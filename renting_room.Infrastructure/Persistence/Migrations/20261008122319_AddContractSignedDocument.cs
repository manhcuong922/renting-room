using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractSignedDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts");

            migrationBuilder.AddColumn<bool>(
                name: "has_signed_document",
                table: "contracts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "signed_document_note",
                table: "contracts",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts",
                sql: "status NOT IN ('Active','Liquidating','Ended') OR effective_date IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "has_signed_document",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "signed_document_note",
                table: "contracts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_activated_snapshot",
                table: "contracts",
                sql: "status NOT IN ('Active','Liquidating','Ended') OR (effective_date IS NOT NULL AND signing_snapshot IS NOT NULL)");
        }
    }
}
