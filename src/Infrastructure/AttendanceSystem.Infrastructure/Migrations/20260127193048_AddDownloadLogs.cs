using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    TotalRecordsDownloaded = table.Column<int>(type: "integer", nullable: false),
                    NewRecordsAdded = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DownloadType = table.Column<int>(type: "integer", nullable: false),
                    InitiatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    InitiatedByUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FromDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ToDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_DeviceId",
                table: "DownloadLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_DownloadType",
                table: "DownloadLogs",
                column: "DownloadType");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_IsSuccessful",
                table: "DownloadLogs",
                column: "IsSuccessful");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadLogs_StartedAt",
                table: "DownloadLogs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadLogs");
        }
    }
}
