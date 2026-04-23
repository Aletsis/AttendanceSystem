using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBranchSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Description",
                table: "Branches",
                newName: "ExternalHost");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Branches",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsExternal",
                table: "Branches",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "IsExternal",
                table: "Branches");

            migrationBuilder.RenameColumn(
                name: "ExternalHost",
                table: "Branches",
                newName: "Description");
        }
    }
}
