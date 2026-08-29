using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WhatYouSay.Migrations
{
    /// <inheritdoc />
    public partial class StoreTimestampsAsSortableUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        // Intentionally empty. DateTimeOffset was already stored as TEXT, so the schema
        // is unchanged; what changed is the format written into it — normalised to UTC so
        // SQLite can ORDER BY it. The migration exists to keep the model snapshot in sync.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
