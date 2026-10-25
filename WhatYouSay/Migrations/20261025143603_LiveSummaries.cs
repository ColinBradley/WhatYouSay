using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class LiveSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing topics keep the reviewed workflow they were created under. Live is
            // only the default for topics created from here on.
            migrationBuilder.AddColumn<string>(
                name: "SummaryPolicy",
                table: "Topics",
                type: "TEXT",
                nullable: false,
                defaultValue: "Reviewed");

            migrationBuilder.AddColumn<string>(
                name: "ArchivedAt",
                table: "SummaryNodes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLive",
                table: "Summaries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Summaries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Summaries_LiveTopicId",
                table: "Summaries",
                column: "TopicId",
                unique: true,
                filter: "\"IsLive\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Summaries_LiveTopicId",
                table: "Summaries");

            migrationBuilder.DropColumn(
                name: "SummaryPolicy",
                table: "Topics");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "SummaryNodes");

            migrationBuilder.DropColumn(
                name: "IsLive",
                table: "Summaries");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Summaries");
        }
    }
}
