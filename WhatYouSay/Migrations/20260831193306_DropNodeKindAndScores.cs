using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class DropNodeKindAndScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SummaryNodes");

            migrationBuilder.DropColumn(
                name: "Objectivity",
                table: "SummaryNodes");

            migrationBuilder.DropColumn(
                name: "Sentiment",
                table: "SummaryNodes");

            migrationBuilder.DropColumn(
                name: "Intensity",
                table: "References");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "SummaryNodes",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "Objectivity",
                table: "SummaryNodes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Sentiment",
                table: "SummaryNodes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Intensity",
                table: "References",
                type: "REAL",
                nullable: true);
        }
    }
}
