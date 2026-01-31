using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeCapToEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "OvertimeCapMinutes",
                table: "Employees",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OvertimeCapType",
                table: "Employees",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OvertimeCapMinutes",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OvertimeCapType",
                table: "Employees");
        }
    }
}
