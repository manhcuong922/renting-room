using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace renting_room.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefundSourceInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_invoice_id",
                table: "invoice_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_lines_source_invoice",
                table: "invoice_lines",
                column: "source_invoice_id",
                filter: "source_invoice_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoice_lines_source_invoice",
                table: "invoice_lines");

            migrationBuilder.DropColumn(
                name: "source_invoice_id",
                table: "invoice_lines");
        }
    }
}
