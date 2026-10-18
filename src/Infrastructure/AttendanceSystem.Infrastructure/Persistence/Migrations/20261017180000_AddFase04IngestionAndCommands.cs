using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFase04IngestionAndCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdmsCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CommandId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CommandText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsDestructive = table.Column<bool>(type: "boolean", nullable: false),
                    DownloadLogId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnCode = table.Column<int>(type: "integer", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmsCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdmsIngestionIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RawData = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmsIngestionIncidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdmsCommands_CreatedAtUtc",
                table: "AdmsCommands",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdmsCommands_SerialNumber_CommandId",
                table: "AdmsCommands",
                columns: new[] { "SerialNumber", "CommandId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdmsCommands_SerialNumber_Status",
                table: "AdmsCommands",
                columns: new[] { "SerialNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AdmsIngestionIncidents_CreatedAtUtc",
                table: "AdmsIngestionIncidents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdmsIngestionIncidents_SerialNumber_Resolved",
                table: "AdmsIngestionIncidents",
                columns: new[] { "SerialNumber", "Resolved" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdmsIngestionIncidents");

            migrationBuilder.DropTable(
                name: "AdmsCommands");
        }
    }
}
