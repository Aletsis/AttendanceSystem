using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCloudSyncAndExternalLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloudDbConnectionString",
                table: "SystemConfiguration",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCloudSyncEnabled",
                table: "SystemConfiguration",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "ExternalAttendanceLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    EmployeeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CheckTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifyMethod = table.Column<int>(type: "integer", nullable: false),
                    CheckType = table.Column<int>(type: "integer", nullable: false),
                    SourceDevice = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StatusId = table.Column<int>(type: "int", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransferredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAttendanceLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAttendanceLogs_BranchCode_StatusId",
                table: "ExternalAttendanceLogs",
                columns: new[] { "BranchCode", "StatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAttendanceLogs_CreatedAt",
                table: "ExternalAttendanceLogs",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalAttendanceLogs");

            migrationBuilder.DropColumn(
                name: "CloudDbConnectionString",
                table: "SystemConfiguration");

            migrationBuilder.DropColumn(
                name: "IsCloudSyncEnabled",
                table: "SystemConfiguration");
        }
    }
}
