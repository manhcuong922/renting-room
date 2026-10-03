using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdHead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "household_head_renter_id",
                table: "contracts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_id_household_head_renter_id",
                table: "contracts",
                columns: new[] { "organization_id", "household_head_renter_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_contracts_renters_organization_id_household_head_renter_id",
                table: "contracts",
                columns: new[] { "organization_id", "household_head_renter_id" },
                principalTable: "renters",
                principalColumns: new[] { "organization_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contracts_renters_organization_id_household_head_renter_id",
                table: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_contracts_organization_id_household_head_renter_id",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "household_head_renter_id",
                table: "contracts");
        }
    }
}
