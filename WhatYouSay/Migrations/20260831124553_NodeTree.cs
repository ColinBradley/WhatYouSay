using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class NodeTree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Topics and points are dropped rather than converted. A converted tree would be
            // two flat levels of Frame over Claim, which is the shape this migration exists to
            // leave behind, and every summary is redrawn from the responses anyway. Responses
            // are the precious data; summaries are cheap. Deleting the summaries with them is
            // what stops References being left pointing at node ids that were never created.
            migrationBuilder.Sql(@"DELETE FROM ""References"";");
            migrationBuilder.Sql(@"DELETE FROM ""Summaries"";");

            migrationBuilder.DropForeignKey(
                name: "FK_References_SummaryTopicPoints_PointId",
                table: "References");

            migrationBuilder.DropTable(
                name: "PointReactions");

            migrationBuilder.DropTable(
                name: "SummaryTopicPoints");

            migrationBuilder.DropTable(
                name: "SummaryTopics");

            migrationBuilder.RenameColumn(
                name: "PointId",
                table: "References",
                newName: "NodeId");

            migrationBuilder.RenameIndex(
                name: "IX_References_PointId",
                table: "References",
                newName: "IX_References_NodeId");

            migrationBuilder.CreateTable(
                name: "SummaryNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SummaryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Sentiment = table.Column<double>(type: "REAL", nullable: true),
                    Objectivity = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummaryNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummaryNodes_Summaries_SummaryId",
                        column: x => x.SummaryId,
                        principalTable: "Summaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SummaryNodes_SummaryNodes_ParentId",
                        column: x => x.ParentId,
                        principalTable: "SummaryNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NodeId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponderTokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NodeReactions_SummaryNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "SummaryNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NodeReactions_NodeId_ResponderTokenHash_Kind",
                table: "NodeReactions",
                columns: ["NodeId", "ResponderTokenHash", "Kind"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummaryNodes_ParentId",
                table: "SummaryNodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_SummaryNodes_SummaryId",
                table: "SummaryNodes",
                column: "SummaryId");

            migrationBuilder.AddForeignKey(
                name: "FK_References_SummaryNodes_NodeId",
                table: "References",
                column: "NodeId",
                principalTable: "SummaryNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_References_SummaryNodes_NodeId",
                table: "References");

            migrationBuilder.DropTable(
                name: "NodeReactions");

            migrationBuilder.DropTable(
                name: "SummaryNodes");

            migrationBuilder.RenameColumn(
                name: "NodeId",
                table: "References",
                newName: "PointId");

            migrationBuilder.RenameIndex(
                name: "IX_References_NodeId",
                table: "References",
                newName: "IX_References_PointId");

            migrationBuilder.CreateTable(
                name: "SummaryTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SummaryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummaryTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummaryTopics_Summaries_SummaryId",
                        column: x => x.SummaryId,
                        principalTable: "Summaries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummaryTopicPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TopicId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Objectivity = table.Column<double>(type: "REAL", nullable: true),
                    Sentiment = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummaryTopicPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummaryTopicPoints_SummaryTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "SummaryTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PointReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PointId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    ResponderTokenHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PointReactions_SummaryTopicPoints_PointId",
                        column: x => x.PointId,
                        principalTable: "SummaryTopicPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PointReactions_PointId_ResponderTokenHash_Kind",
                table: "PointReactions",
                columns: ["PointId", "ResponderTokenHash", "Kind"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummaryTopicPoints_TopicId",
                table: "SummaryTopicPoints",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SummaryTopics_SummaryId",
                table: "SummaryTopics",
                column: "SummaryId");

            migrationBuilder.AddForeignKey(
                name: "FK_References_SummaryTopicPoints_PointId",
                table: "References",
                column: "PointId",
                principalTable: "SummaryTopicPoints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
