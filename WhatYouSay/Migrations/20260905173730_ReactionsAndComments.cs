using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class ReactionsAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorTokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    IsHidden = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeComments_SummaryNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "SummaryNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeComments_NodeId",
                table: "NodeComments",
                column: "NodeId");

            // Misrepresents leaves the reaction set: it was a button nobody presses, and
            // what people actually write does not reduce to a flag. The ones that carried a
            // note become comments, which is where an objection lives now. A bare flag has
            // no words to carry across and goes with the kind.
            migrationBuilder.Sql(
                """
                INSERT INTO "NodeComments" ("NodeId", "AuthorTokenHash", "Author", "Body", "IsHidden", "CreatedAt")
                SELECT r."NodeId",
                       r."ResponderTokenHash",
                       (SELECT p."Author" FROM "Responses" AS p
                        WHERE p."AuthTokenHash" = r."ResponderTokenHash" AND NOT p."IsDeleted"),
                       r."Note",
                       0,
                       r."CreatedAt"
                FROM "NodeReactions" AS r
                WHERE r."Kind" = 'Misrepresents' AND r."Note" IS NOT NULL AND trim(r."Note") <> '';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "NodeReactions" WHERE "Kind" = 'Misrepresents';
                """);

            migrationBuilder.DropColumn(
                name: "Note",
                table: "NodeReactions");

            migrationBuilder.RenameColumn(
                name: "ResponderTokenHash",
                table: "NodeReactions",
                newName: "ReactorTokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_NodeReactions_NodeId_ResponderTokenHash_Kind",
                table: "NodeReactions",
                newName: "IX_NodeReactions_NodeId_ReactorTokenHash_Kind");
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NodeComments");

            migrationBuilder.RenameColumn(
                name: "ReactorTokenHash",
                table: "NodeReactions",
                newName: "ResponderTokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_NodeReactions_NodeId_ReactorTokenHash_Kind",
                table: "NodeReactions",
                newName: "IX_NodeReactions_NodeId_ResponderTokenHash_Kind");

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "NodeReactions",
                type: "TEXT",
                nullable: true);
        }
    }
}
