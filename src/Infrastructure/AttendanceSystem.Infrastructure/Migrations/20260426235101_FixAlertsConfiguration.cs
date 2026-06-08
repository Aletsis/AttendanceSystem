using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixAlertsConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlertEmailRecipients",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AreAlertsEnabled",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpEnableSsl",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "SystemConfiguration",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUser",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlertEmailRecipients",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "AreAlertsEnabled",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "SmtpEnableSsl",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "SmtpUser",
                table: "SystemConfiguration");
        }
    }
}
