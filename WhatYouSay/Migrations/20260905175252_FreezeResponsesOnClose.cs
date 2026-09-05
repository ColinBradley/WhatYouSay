using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class FreezeResponsesOnClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFrozen",
                table: "Responses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // The freeze used to be the topic being closed. Existing rows carry that across,
            // or every response on a closed topic becomes editable again.
            migrationBuilder.Sql(
                """
                UPDATE "Responses" SET "IsFrozen" = 1
                WHERE "TopicId" IN (SELECT "Id" FROM "Topics" WHERE NOT "IsAcceptingResponses");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFrozen",
                table: "Responses");
        }
    }
}
