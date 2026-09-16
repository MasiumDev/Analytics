using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramProfessionalProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfessionalAccountType",
                table: "InstagramAccounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfessionalAccountType",
                table: "InstagramAccounts");
        }
    }
}
