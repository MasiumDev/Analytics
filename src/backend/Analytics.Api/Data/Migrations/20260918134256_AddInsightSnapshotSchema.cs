using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsightSnapshotSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountInsightSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstagramAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewsCount = table.Column<long>(type: "bigint", nullable: true),
                    ReachCount = table.Column<long>(type: "bigint", nullable: true),
                    FollowerCount = table.Column<long>(type: "bigint", nullable: true),
                    ProfileViewsCount = table.Column<long>(type: "bigint", nullable: true),
                    WebsiteClicksCount = table.Column<long>(type: "bigint", nullable: true),
                    AccountsEngagedCount = table.Column<long>(type: "bigint", nullable: true),
                    TotalInteractionsCount = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceTimestampUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountInsightSnapshots", x => x.Id);
                    table.CheckConstraint("CK_AccountInsightSnapshots_NonNegativeMetrics", "([ViewsCount] IS NULL OR [ViewsCount] >= 0) AND ([ReachCount] IS NULL OR [ReachCount] >= 0) AND ([FollowerCount] IS NULL OR [FollowerCount] >= 0) AND ([ProfileViewsCount] IS NULL OR [ProfileViewsCount] >= 0) AND ([WebsiteClicksCount] IS NULL OR [WebsiteClicksCount] >= 0) AND ([AccountsEngagedCount] IS NULL OR [AccountsEngagedCount] >= 0) AND ([TotalInteractionsCount] IS NULL OR [TotalInteractionsCount] >= 0)");
                    table.ForeignKey(
                        name: "FK_AccountInsightSnapshots_InstagramAccounts_InstagramAccountId",
                        column: x => x.InstagramAccountId,
                        principalTable: "InstagramAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaInsightSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstagramMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViewsCount = table.Column<long>(type: "bigint", nullable: true),
                    ReachCount = table.Column<long>(type: "bigint", nullable: true),
                    LikesCount = table.Column<long>(type: "bigint", nullable: true),
                    CommentsCount = table.Column<long>(type: "bigint", nullable: true),
                    SavesCount = table.Column<long>(type: "bigint", nullable: true),
                    SharesCount = table.Column<long>(type: "bigint", nullable: true),
                    TotalInteractionsCount = table.Column<long>(type: "bigint", nullable: true),
                    AverageWatchTimeMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    TotalWatchTimeMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SourceTimestampUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaInsightSnapshots", x => x.Id);
                    table.CheckConstraint("CK_MediaInsightSnapshots_NonNegativeMetrics", "([ViewsCount] IS NULL OR [ViewsCount] >= 0) AND ([ReachCount] IS NULL OR [ReachCount] >= 0) AND ([LikesCount] IS NULL OR [LikesCount] >= 0) AND ([CommentsCount] IS NULL OR [CommentsCount] >= 0) AND ([SavesCount] IS NULL OR [SavesCount] >= 0) AND ([SharesCount] IS NULL OR [SharesCount] >= 0) AND ([TotalInteractionsCount] IS NULL OR [TotalInteractionsCount] >= 0) AND ([AverageWatchTimeMilliseconds] IS NULL OR [AverageWatchTimeMilliseconds] >= 0) AND ([TotalWatchTimeMilliseconds] IS NULL OR [TotalWatchTimeMilliseconds] >= 0)");
                    table.ForeignKey(
                        name: "FK_MediaInsightSnapshots_InstagramMedia_InstagramMediaId",
                        column: x => x.InstagramMediaId,
                        principalTable: "InstagramMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountInsightSnapshots_CapturedAtUtc",
                table: "AccountInsightSnapshots",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AccountInsightSnapshots_InstagramAccountId_CapturedAtUtc",
                table: "AccountInsightSnapshots",
                columns: new[] { "InstagramAccountId", "CapturedAtUtc" },
                unique: true,
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MediaInsightSnapshots_CapturedAtUtc",
                table: "MediaInsightSnapshots",
                column: "CapturedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MediaInsightSnapshots_InstagramMediaId_CapturedAtUtc",
                table: "MediaInsightSnapshots",
                columns: new[] { "InstagramMediaId", "CapturedAtUtc" },
                unique: true,
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountInsightSnapshots");

            migrationBuilder.DropTable(
                name: "MediaInsightSnapshots");
        }
    }
}
