using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class SummaryIsAgentEditable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAgentEditable",
                table: "Summaries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Write access used to be IsDraft doing double duty, so existing rows carry
            // that meaning across rather than locking the agent out of drafts
            // it could write to yesterday.
            migrationBuilder.Sql(
                """
                UPDATE "Summaries" SET "IsAgentEditable" = "IsDraft";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAgentEditable",
                table: "Summaries");
        }
    }
}
