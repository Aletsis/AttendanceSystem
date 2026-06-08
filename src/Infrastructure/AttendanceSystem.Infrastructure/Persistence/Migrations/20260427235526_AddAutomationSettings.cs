using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "AutoBackupTime",
                table: "SystemConfiguration",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoReportEmails",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "AutoReportTime",
                table: "SystemConfiguration",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoBackupEnabled",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoReportEnabled",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoBackupTime",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "AutoReportEmails",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "AutoReportTime",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "IsAutoBackupEnabled",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "IsAutoReportEnabled",
                table: "SystemConfiguration");
        }
    }
}
