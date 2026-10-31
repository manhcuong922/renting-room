using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractBillingGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contract_billing_gaps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    closing_period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_count = table.Column<int>(type: "integer", nullable: false),
                    resumed_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_billing_gaps", x => x.id);
                    table.CheckConstraint("ck_contract_billing_gaps_range", "from_date <= to_date AND period_count BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_contract_billing_gaps_contracts_organization_id_contract_id",
                        columns: x => new { x.organization_id, x.contract_id },
                        principalTable: "contracts",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contract_billing_gaps_invoices_organization_id_resumed_invo",
                        columns: x => new { x.organization_id, x.resumed_invoice_id },
                        principalTable: "invoices",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contract_billing_gaps_contract",
                table: "contract_billing_gaps",
                columns: new[] { "organization_id", "contract_id", "from_date" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_billing_gaps_organization_id_resumed_invoice_id",
                table: "contract_billing_gaps",
                columns: new[] { "organization_id", "resumed_invoice_id" });

            migrationBuilder.CreateIndex(
                name: "ux_contract_billing_gaps_invoice",
                table: "contract_billing_gaps",
                column: "resumed_invoice_id",
                unique: true,
                filter: "resumed_invoice_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contract_billing_gaps");
        }
    }
}
