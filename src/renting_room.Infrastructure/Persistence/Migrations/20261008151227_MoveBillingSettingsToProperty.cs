using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveBillingSettingsToProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_properties_anchor_day",
                table: "properties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_settings",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "billing_anchor_day",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "charge_mode",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "payment_due_days",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "proration_mode",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "default_proration_mode",
                table: "properties",
                newName: "proration_mode");

            migrationBuilder.RenameColumn(
                name: "default_payment_due_days",
                table: "properties",
                newName: "payment_due_days");

            migrationBuilder.RenameColumn(
                name: "default_charge_mode",
                table: "properties",
                newName: "charge_mode");

            migrationBuilder.RenameColumn(
                name: "default_billing_anchor_day",
                table: "properties",
                newName: "billing_anchor_day");

            migrationBuilder.AddColumn<string>(
                name: "billing_schedule",
                table: "properties",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<DateOnly>(
                name: "billing_start_date",
                table: "contracts",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // PR-BR-09: ngày chốt 1–28; lịch kỳ thu ban đầu = cài đặt hiện tại của khu. HĐ theo khu (bỏ cài đặt riêng — CT-BR-04).
            migrationBuilder.Sql("UPDATE properties SET billing_anchor_day = LEAST(billing_anchor_day, 28);");
            migrationBuilder.Sql("""
                UPDATE properties SET billing_schedule = jsonb_build_array(jsonb_build_object(
                    'effectiveFrom', '0001-01-01', 'anchorDay', billing_anchor_day, 'chargeMode', charge_mode, 'adjustDays', 0));
                """);
            // K5: "Tính tiền từ ngày" mặc định = ngày bắt đầu.
            migrationBuilder.Sql("UPDATE contracts SET billing_start_date = start_date;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_properties_anchor_day",
                table: "properties",
                sql: "billing_anchor_day BETWEEN 1 AND 28");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_billing_start",
                table: "contracts",
                sql: "billing_start_date >= start_date AND (end_date IS NULL OR billing_start_date <= end_date)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_settings",
                table: "contracts",
                sql: "deposit_amount >= 0 AND copies_count BETWEEN 1 AND 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_properties_anchor_day",
                table: "properties");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_billing_start",
                table: "contracts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contracts_settings",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "billing_schedule",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "billing_start_date",
                table: "contracts");

            migrationBuilder.RenameColumn(
                name: "proration_mode",
                table: "properties",
                newName: "default_proration_mode");

            migrationBuilder.RenameColumn(
                name: "payment_due_days",
                table: "properties",
                newName: "default_payment_due_days");

            migrationBuilder.RenameColumn(
                name: "charge_mode",
                table: "properties",
                newName: "default_charge_mode");

            migrationBuilder.RenameColumn(
                name: "billing_anchor_day",
                table: "properties",
                newName: "default_billing_anchor_day");

            migrationBuilder.AddColumn<int>(
                name: "billing_anchor_day",
                table: "contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "charge_mode",
                table: "contracts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "payment_due_days",
                table: "contracts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "proration_mode",
                table: "contracts",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_properties_anchor_day",
                table: "properties",
                sql: "default_billing_anchor_day BETWEEN 1 AND 31");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_settings",
                table: "contracts",
                sql: "billing_anchor_day BETWEEN 1 AND 31 AND deposit_amount >= 0 AND copies_count BETWEEN 1 AND 10");
        }
    }
}
