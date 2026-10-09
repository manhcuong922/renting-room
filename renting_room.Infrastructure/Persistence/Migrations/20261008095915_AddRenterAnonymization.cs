using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRenterAnonymization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                table: "renters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "auto_anonymize_enabled",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "personal_data_retention_months",
                table: "organizations",
                type: "integer",
                nullable: false,
                defaultValue: 36);

            migrationBuilder.AddCheckConstraint(
                name: "ck_organizations_retention_months",
                table: "organizations",
                sql: "personal_data_retention_months BETWEEN 36 AND 120");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_organizations_retention_months",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                table: "renters");

            migrationBuilder.DropColumn(
                name: "auto_anonymize_enabled",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "personal_data_retention_months",
                table: "organizations");
        }
    }
}
