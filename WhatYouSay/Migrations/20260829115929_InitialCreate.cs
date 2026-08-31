using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Surveys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    AdminPasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    SummariserTokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsPubliclyListed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAcceptingResponses = table.Column<bool>(type: "INTEGER", nullable: false),
                    AreResponsesPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResponseIdentity = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Surveys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Responses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    AuthTokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Responses_Surveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "Surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Summaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SurveyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    IsDraft = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublic = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Summaries_Surveys_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "Surveys",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummaryTopics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SummaryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
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
                    Sentiment = table.Column<double>(type: "REAL", nullable: true),
                    Objectivity = table.Column<double>(type: "REAL", nullable: true)
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
                    ResponderTokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "References",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PointId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResponseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Quote = table.Column<string>(type: "TEXT", nullable: false),
                    StartIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    EndIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Intensity = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_References", x => x.Id);
                    table.ForeignKey(
                        name: "FK_References_Responses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "Responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_References_SummaryTopicPoints_PointId",
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
                name: "IX_References_PointId",
                table: "References",
                column: "PointId");

            migrationBuilder.CreateIndex(
                name: "IX_References_ResponseId",
                table: "References",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_Responses_AuthTokenHash",
                table: "Responses",
                column: "AuthTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_Responses_SurveyId",
                table: "Responses",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_Summaries_SurveyId",
                table: "Summaries",
                column: "SurveyId");

            migrationBuilder.CreateIndex(
                name: "IX_SummaryTopicPoints_TopicId",
                table: "SummaryTopicPoints",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_SummaryTopics_SummaryId",
                table: "SummaryTopics",
                column: "SummaryId");

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_Code",
                table: "Surveys",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Surveys_SummariserTokenHash",
                table: "Surveys",
                column: "SummariserTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PointReactions");

            migrationBuilder.DropTable(
                name: "References");

            migrationBuilder.DropTable(
                name: "Responses");

            migrationBuilder.DropTable(
                name: "SummaryTopicPoints");

            migrationBuilder.DropTable(
                name: "SummaryTopics");

            migrationBuilder.DropTable(
                name: "Summaries");

            migrationBuilder.DropTable(
                name: "Surveys");
        }
    }
}
