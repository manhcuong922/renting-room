using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractRoomTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "room_since",
                table: "contracts",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "contract_room_moves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_contract_room_moves", x => x.id);
                    table.CheckConstraint("ck_contract_room_moves_dates", "to_date >= from_date");
                    table.ForeignKey(
                        name: "fk_contract_room_moves_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contract_room_moves_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_room_moves_organization_id_contract_id",
                table: "contract_room_moves",
                columns: new[] { "organization_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_room_moves_room",
                table: "contract_room_moves",
                columns: new[] { "organization_id", "room_id", "to_date" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_room_moves_room_id",
                table: "contract_room_moves",
                column: "room_id");

            // CT-BR-14: HĐ hiện có ở phòng từ ngày bắt đầu; "1 phòng 1 HĐ" tính từ ngày vào phòng hiện tại (chuyển phòng đổi ngày này).
            migrationBuilder.Sql("UPDATE contracts SET room_since = start_date;");
            migrationBuilder.Sql("ALTER TABLE contracts DROP CONSTRAINT ex_contracts_room_period;");
            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT ex_contracts_room_period
                EXCLUDE USING gist (room_id WITH =, daterange(room_since, actual_end_date, '[]') WITH &&)
                WHERE (status IN ('Active', 'Liquidating', 'Ended'));
                """);
            // Lịch sử chuyển phòng không chồng nhau trên cùng phòng (chồng với HĐ hiện tại: Application kiểm khi đã khóa phòng).
            migrationBuilder.Sql("""
                ALTER TABLE contract_room_moves ADD CONSTRAINT ex_contract_room_moves_period
                EXCLUDE USING gist (room_id WITH =, daterange(from_date, to_date, '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE contracts DROP CONSTRAINT ex_contracts_room_period;");
            migrationBuilder.Sql("""
                ALTER TABLE contracts ADD CONSTRAINT ex_contracts_room_period
                EXCLUDE USING gist (room_id WITH =, daterange(start_date, actual_end_date, '[]') WITH &&)
                WHERE (status IN ('Active', 'Liquidating', 'Ended'));
                """);

            migrationBuilder.DropTable(
                name: "contract_room_moves");

            migrationBuilder.DropColumn(
                name: "room_since",
                table: "contracts");
        }
    }
}
