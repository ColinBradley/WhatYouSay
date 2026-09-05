using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class RenameSurveyToTopic : Migration
    {
        // Scaffolded as DropTable + CreateTable, which empties Topics and cascades that
        // through Responses and Summaries. Rewritten as renames: the rows are the point.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Surveys",
                newName: "Topics");

            migrationBuilder.RenameIndex(
                name: "IX_Surveys_Code",
                table: "Topics",
                newName: "IX_Topics_Code");

            migrationBuilder.RenameIndex(
                name: "IX_Surveys_SummariserTokenHash",
                table: "Topics",
                newName: "IX_Topics_SummariserTokenHash");

            migrationBuilder.RenameColumn(
                name: "SurveyId",
                table: "Responses",
                newName: "TopicId");

            migrationBuilder.RenameIndex(
                name: "IX_Responses_SurveyId",
                table: "Responses",
                newName: "IX_Responses_TopicId");

            migrationBuilder.RenameColumn(
                name: "SurveyId",
                table: "Summaries",
                newName: "TopicId");

            migrationBuilder.RenameIndex(
                name: "IX_Summaries_SurveyId",
                table: "Summaries",
                newName: "IX_Summaries_TopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_Summaries_TopicId",
                table: "Summaries",
                newName: "IX_Summaries_SurveyId");

            migrationBuilder.RenameColumn(
                name: "TopicId",
                table: "Summaries",
                newName: "SurveyId");

            migrationBuilder.RenameIndex(
                name: "IX_Responses_TopicId",
                table: "Responses",
                newName: "IX_Responses_SurveyId");

            migrationBuilder.RenameColumn(
                name: "TopicId",
                table: "Responses",
                newName: "SurveyId");

            migrationBuilder.RenameIndex(
                name: "IX_Topics_SummariserTokenHash",
                table: "Topics",
                newName: "IX_Surveys_SummariserTokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_Topics_Code",
                table: "Topics",
                newName: "IX_Surveys_Code");

            migrationBuilder.RenameTable(
                name: "Topics",
                newName: "Surveys");
        }
    }
}
