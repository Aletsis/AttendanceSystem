using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Marcajes intermedios excluidos por RH (sin posición en la jornada), p. ej. un marcaje sin pareja.
    /// </summary>
    public partial class AddExcludedIntermediatePunches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(name: "ExcludedPunches", table: "AttendanceAdjustments", type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ExcludedPunches", table: "AttendanceAdjustments");
        }
    }
}
