using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CollectionReductions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "collection_reductions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_before = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    amount_after = table.Column<decimal>(type: "numeric(18,0)", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collection_reductions", x => x.id);
                    table.CheckConstraint("ck_collection_reductions_amounts", "amount_after >= 0 AND amount_after < amount_before");
                    table.ForeignKey(
                        name: "fk_collection_reductions_invoices_organization_id_contract_id_",
                        columns: x => new { x.organization_id, x.contract_id, x.invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "contract_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_collection_reductions_invoice",
                table: "collection_reductions",
                columns: new[] { "organization_id", "invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ix_collection_reductions_organization_id_contract_id_invoice_id",
                table: "collection_reductions",
                columns: new[] { "organization_id", "contract_id", "invoice_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_reductions");
        }
    }
}
