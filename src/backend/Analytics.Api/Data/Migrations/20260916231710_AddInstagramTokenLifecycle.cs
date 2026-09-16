using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramTokenLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EncryptedAccessToken",
                table: "InstagramCredentials",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "ConnectionStatus",
                table: "InstagramAccounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.Sql(
                """
                UPDATE account
                SET account.ConnectionStatus = 'Connected'
                FROM InstagramAccounts AS account
                WHERE EXISTS (
                    SELECT 1
                    FROM InstagramCredentials AS credential
                    WHERE credential.InstagramAccountId = account.Id
                      AND credential.Status = 'Active');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectionStatus",
                table: "InstagramAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedAccessToken",
                table: "InstagramCredentials",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
