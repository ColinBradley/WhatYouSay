using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class ResponseTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Named topics already record timing, so they become Recorded.
            // Anonymous topics have recorded none, so they become NotRecorded and keep it.
            // Their responses gain no times, and nothing reconstructs the order they arrived in.
            migrationBuilder.AddColumn<string>(
                name: "ResponseTiming",
                table: "Topics",
                type: "TEXT",
                nullable: false,
                defaultValue: "Recorded");

            migrationBuilder.Sql(
                """
                UPDATE "Topics" SET "ResponseTiming" = 'NotRecorded' WHERE "ResponseIdentity" = 'Anonymous';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResponseTiming",
                table: "Topics");
        }
    }
}
