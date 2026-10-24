using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Caso 13: los marcajes manuales guardan quién los registró, cuándo y por qué.
    /// </summary>
    public partial class ManualPunchAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegisteredBy",
                table: "AttendanceRecords",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegisteredAt",
                table: "AttendanceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AttendanceRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RegisteredBy", table: "AttendanceRecords");
            migrationBuilder.DropColumn(name: "RegisteredAt", table: "AttendanceRecords");
            migrationBuilder.DropColumn(name: "Reason", table: "AttendanceRecords");
        }
    }
}
