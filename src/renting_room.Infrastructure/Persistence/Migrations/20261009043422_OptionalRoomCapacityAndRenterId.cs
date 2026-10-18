using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptionalRoomCapacityAndRenterId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_max_occupants",
                table: "rooms");

            migrationBuilder.AlterColumn<int>(
                name: "max_occupants",
                table: "rooms",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "id_type",
                table: "renters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "id_number_last4",
                table: "renters",
                type: "character varying(4)",
                maxLength: 4,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "id_number_hash",
                table: "renters",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<byte[]>(
                name: "id_number_encrypted",
                table: "renters",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_max_occupants",
                table: "rooms",
                sql: "max_occupants IS NULL OR max_occupants BETWEEN 1 AND 20");

            // RT-BR-06: hồ sơ đã ẩn danh trước đây giữ giấy tờ giả (hash riêng, số rỗng) ⇒ chuyển về "không có giấy tờ".
            migrationBuilder.Sql("""
                UPDATE renters SET id_type = NULL, id_number_encrypted = NULL, id_number_hash = NULL, id_number_last4 = NULL
                WHERE anonymized_at IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_renters_id_document",
                table: "renters",
                sql: "(id_type IS NULL) = (id_number_hash IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_max_occupants",
                table: "rooms");

            migrationBuilder.DropCheckConstraint(
                name: "ck_renters_id_document",
                table: "renters");

            migrationBuilder.AlterColumn<int>(
                name: "max_occupants",
                table: "rooms",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_type",
                table: "renters",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_number_last4",
                table: "renters",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "id_number_hash",
                table: "renters",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character(64)",
                oldFixedLength: true,
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "id_number_encrypted",
                table: "renters",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_max_occupants",
                table: "rooms",
                sql: "max_occupants BETWEEN 1 AND 20");
        }
    }
}
