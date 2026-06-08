using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSystemConfigurationAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AlertEmailRecipients",
                table: "SystemConfiguration",
                newName: "SystemFailureAlertEmails");

            migrationBuilder.AlterColumn<bool>(
                name: "AreAlertsEnabled",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<string>(
                name: "AbsenceAlertEmails",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateAlertEmails",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsenceAlertEmails",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "LateAlertEmails",
                table: "SystemConfiguration");

            migrationBuilder.RenameColumn(
                name: "SystemFailureAlertEmails",
                table: "SystemConfiguration",
                newName: "AlertEmailRecipients");

            migrationBuilder.AlterColumn<bool>(
                name: "AreAlertsEnabled",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }
    }
}
