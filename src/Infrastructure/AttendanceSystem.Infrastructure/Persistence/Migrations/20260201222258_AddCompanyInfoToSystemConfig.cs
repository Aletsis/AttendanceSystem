using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyInfoToSystemConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CompanyLogo",
                table: "SystemConfiguration",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "SystemConfiguration",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Mi Empresa");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyLogo",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "SystemConfiguration");
        }
    }
}
