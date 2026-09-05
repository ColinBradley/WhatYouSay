using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class SummaryNodeOrdinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "SummaryNodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing rows were written depth-first, so key order is their display order.
            // Freeze that into Ordinal before anything can move a node and make it a lie.
            // ParentId is compared through IS to keep roots grouped: NULL = NULL is NULL.
            migrationBuilder.Sql(@"
                UPDATE ""SummaryNodes"" SET ""Ordinal"" = (
                    SELECT COUNT(*) FROM ""SummaryNodes"" AS earlier
                    WHERE earlier.""SummaryId"" = ""SummaryNodes"".""SummaryId""
                      AND earlier.""ParentId"" IS ""SummaryNodes"".""ParentId""
                      AND earlier.""Id"" < ""SummaryNodes"".""Id""
                );");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "SummaryNodes");
        }
    }
}
