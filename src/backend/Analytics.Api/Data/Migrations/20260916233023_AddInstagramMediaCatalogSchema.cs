using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramMediaCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountCurrentStats",
                columns: table => new
                {
                    InstagramAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FollowersCount = table.Column<long>(type: "bigint", nullable: true),
                    FollowsCount = table.Column<long>(type: "bigint", nullable: true),
                    MediaCount = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountCurrentStats", x => x.InstagramAccountId);
                    table.ForeignKey(
                        name: "FK_AccountCurrentStats_InstagramAccounts_InstagramAccountId",
                        column: x => x.InstagramAccountId,
                        principalTable: "InstagramAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstagramMedia",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstagramAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstagramMediaId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Permalink = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(2200)", maxLength: 2200, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstagramMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstagramMedia_InstagramAccounts_InstagramAccountId",
                        column: x => x.InstagramAccountId,
                        principalTable: "InstagramAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaCurrentStats",
                columns: table => new
                {
                    InstagramMediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LikeCount = table.Column<long>(type: "bigint", nullable: true),
                    CommentsCount = table.Column<long>(type: "bigint", nullable: true),
                    SavesCount = table.Column<long>(type: "bigint", nullable: true),
                    SharesCount = table.Column<long>(type: "bigint", nullable: true),
                    ReachCount = table.Column<long>(type: "bigint", nullable: true),
                    PlaysCount = table.Column<long>(type: "bigint", nullable: true),
                    CapturedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaCurrentStats", x => x.InstagramMediaId);
                    table.ForeignKey(
                        name: "FK_MediaCurrentStats_InstagramMedia_InstagramMediaId",
                        column: x => x.InstagramMediaId,
                        principalTable: "InstagramMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstagramMedia_InstagramAccountId_InstagramMediaId",
                table: "InstagramMedia",
                columns: new[] { "InstagramAccountId", "InstagramMediaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstagramMedia_InstagramAccountId_PublishedAtUtc",
                table: "InstagramMedia",
                columns: new[] { "InstagramAccountId", "PublishedAtUtc" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountCurrentStats");

            migrationBuilder.DropTable(
                name: "MediaCurrentStats");

            migrationBuilder.DropTable(
                name: "InstagramMedia");
        }
    }
}
