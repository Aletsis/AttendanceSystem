using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftContinuousRounding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoundingInterval",
                table: "Shifts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RoundingsEnabled",
                table: "Shifts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RoundingInterval",
                table: "DailyAttendances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RoundingsEnabled",
                table: "DailyAttendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoundingInterval",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "RoundingsEnabled",
                table: "Shifts");

            migrationBuilder.DropColumn(
                name: "RoundingInterval",
                table: "DailyAttendances");

            migrationBuilder.DropColumn(
                name: "RoundingsEnabled",
                table: "DailyAttendances");
        }
    }
}
