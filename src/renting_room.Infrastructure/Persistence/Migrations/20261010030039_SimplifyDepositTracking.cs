using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyDepositTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M08 PM-BR-32 (10/10/2026): bỏ sổ cọc — cọc chỉ là trạng thái trên HĐ. DB dev có thể còn bảng / cột của sổ cọc cũ (migration đã gỡ).
            migrationBuilder.Sql("DROP TABLE IF EXISTS deposit_transactions;");
            migrationBuilder.Sql("ALTER TABLE contracts DROP COLUMN IF EXISTS deposit_hold_until;");
            migrationBuilder.Sql("UPDATE payments SET kind = 'Receipt' WHERE kind = 'Deposit';");

            migrationBuilder.AddColumn<string>(
                name: "deposit_status",
                table: "contracts",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "Holding");

            migrationBuilder.AddColumn<DateOnly>(
                name: "deposit_refunded_on",
                table: "contracts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_refunded_amount",
                table: "contracts",
                type: "numeric(18,0)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_note",
                table: "contracts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contracts_deposit_refund",
                table: "contracts",
                sql: "deposit_status <> 'Refunded' OR (deposit_refunded_on IS NOT NULL AND deposit_refunded_amount BETWEEN 0 AND deposit_amount)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_contracts_deposit_refund", table: "contracts");
            migrationBuilder.DropColumn(name: "deposit_status", table: "contracts");
            migrationBuilder.DropColumn(name: "deposit_refunded_on", table: "contracts");
            migrationBuilder.DropColumn(name: "deposit_refunded_amount", table: "contracts");
            migrationBuilder.DropColumn(name: "deposit_note", table: "contracts");
        }
    }
}
