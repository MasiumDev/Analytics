using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaStatsTimestampsAndImportStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE MediaImportCheckpoints SET Status = 'Succeeded' WHERE Status = 'Completed';");

            migrationBuilder.RenameColumn(
                name: "CapturedAtUtc",
                table: "MediaCurrentStats",
                newName: "ReceivedAtUtc");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SourceTimestampUtc",
                table: "MediaCurrentStats",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE MediaImportCheckpoints
                SET Status = CASE
                    WHEN Status IN ('Succeeded', 'Partial') THEN 'Completed'
                    WHEN Status = 'Queued' THEN 'Running'
                    ELSE Status
                END;
                """);

            migrationBuilder.DropColumn(
                name: "SourceTimestampUtc",
                table: "MediaCurrentStats");

            migrationBuilder.RenameColumn(
                name: "ReceivedAtUtc",
                table: "MediaCurrentStats",
                newName: "CapturedAtUtc");
        }
    }
}
