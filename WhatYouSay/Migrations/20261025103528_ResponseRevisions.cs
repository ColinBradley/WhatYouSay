using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class ResponseRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_References_ResponseId",
                table: "References");

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "Responses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "References",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ResponseRevisions",
                columns: table => new
                {
                    ResponseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResponseRevisions", x => new { x.ResponseId, x.Number });
                    table.ForeignKey(
                        name: "FK_ResponseRevisions_Responses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "Responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Each existing response becomes its own first revision, before its text columns
            // are dropped below.
            // Every existing reference was located in that text, so each one keeps its quote
            // and offsets by pointing at revision 1.
            // Revision 1 takes the submission time.
            // The last-edit time is dropped, because the wording from before that edit was
            // never stored.
            // Withdrawn responses are included, because their references still need a revision
            // to name. Reads hide them by filtering on the response.
            migrationBuilder.Sql(
                """
                INSERT INTO "ResponseRevisions" ("ResponseId", "Number", "Body", "CreatedAt")
                SELECT "Id", 1, "Body", "CreatedAt" FROM "Responses";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_References_ResponseId_RevisionNumber",
                table: "References",
                columns: new[] { "ResponseId", "RevisionNumber" });

            migrationBuilder.AddForeignKey(
                name: "FK_References_ResponseRevisions_ResponseId_RevisionNumber",
                table: "References",
                columns: new[] { "ResponseId", "RevisionNumber" },
                principalTable: "ResponseRevisions",
                principalColumns: new[] { "ResponseId", "Number" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "Body",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Responses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "Responses",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedAt",
                table: "Responses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedAt",
                table: "Responses",
                type: "TEXT",
                nullable: true);

            // Only the current wording survives a downgrade. References to earlier wordings
            // keep their quotes, and rendering drops a highlight whose offsets no longer match.
            migrationBuilder.Sql(
                """
                UPDATE "Responses" AS r
                SET "Body" = (
                        SELECT v."Body" FROM "ResponseRevisions" AS v
                        WHERE v."ResponseId" = r."Id" AND v."Number" = r."RevisionNumber"),
                    "CreatedAt" = (
                        SELECT v."CreatedAt" FROM "ResponseRevisions" AS v
                        WHERE v."ResponseId" = r."Id" AND v."Number" = 1),
                    "UpdatedAt" = (
                        SELECT v."CreatedAt" FROM "ResponseRevisions" AS v
                        WHERE v."ResponseId" = r."Id" AND v."Number" = r."RevisionNumber" AND v."Number" > 1);
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_References_ResponseRevisions_ResponseId_RevisionNumber",
                table: "References");

            migrationBuilder.DropTable(
                name: "ResponseRevisions");

            migrationBuilder.DropIndex(
                name: "IX_References_ResponseId_RevisionNumber",
                table: "References");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "Responses");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "References");

            migrationBuilder.CreateIndex(
                name: "IX_References_ResponseId",
                table: "References",
                column: "ResponseId");
        }
    }
}
