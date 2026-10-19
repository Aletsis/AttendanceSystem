using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthoritativeTimeZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Devices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Branches",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultTimeZoneId",
                table: "SystemConfiguration",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Mexico_City");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "DailyAttendances",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "DefaultTimeZoneId",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "DailyAttendances");
        }
    }
}
