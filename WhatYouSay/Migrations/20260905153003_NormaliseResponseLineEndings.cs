using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class NormaliseResponseLineEndings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Offsets first, while Body still holds the carriage returns they were taken
            // against: each one removed before a position shifts that position left by one.
            // Character positions here are SQLite's, which are UTF-16 offsets only for
            // text inside the BMP; an astral character before the quote skews the count.
            // Rendering already guards a reference whose offsets no longer select its
            // quote by dropping the highlight, so the worst case is cosmetic.
            migrationBuilder.Sql(
                """
                UPDATE "References" AS r
                SET "StartIndex" = "StartIndex" - (
                        SELECT length(substr(p."Body", 1, r."StartIndex"))
                             - length(replace(substr(p."Body", 1, r."StartIndex"), char(13), ''))
                        FROM "Responses" AS p WHERE p."Id" = r."ResponseId"),
                    "EndIndex" = "EndIndex" - (
                        SELECT length(substr(p."Body", 1, r."EndIndex"))
                             - length(replace(substr(p."Body", 1, r."EndIndex"), char(13), ''))
                        FROM "Responses" AS p WHERE p."Id" = r."ResponseId")
                WHERE EXISTS (
                    SELECT 1 FROM "Responses" AS p
                    WHERE p."Id" = r."ResponseId" AND p."Body" LIKE '%' || char(13) || '%');
                """);

            migrationBuilder.Sql(
                """
                UPDATE "References"
                SET "Quote" = replace(replace("Quote", char(13) || char(10), char(10)), char(13), char(10))
                WHERE "Quote" LIKE '%' || char(13) || '%';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Responses"
                SET "Body" = replace(replace("Body", char(13) || char(10), char(10)), char(13), char(10))
                WHERE "Body" LIKE '%' || char(13) || '%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Which line feeds were carriage returns is not recorded, so this cannot be
            // undone. Left empty rather than guessing and corrupting offsets a second time.
        }
    }
}
