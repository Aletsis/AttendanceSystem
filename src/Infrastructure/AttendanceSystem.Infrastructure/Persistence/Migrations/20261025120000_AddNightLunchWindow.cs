using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ventana de comida para turnos nocturnos (puede cruzar medianoche); por defecto 23:00–04:00.
    /// </summary>
    public partial class AddNightLunchWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(name: "NightLunchWindowStart", table: "SystemConfiguration", type: "interval", nullable: false, defaultValue: new TimeSpan(23, 0, 0));
            migrationBuilder.AddColumn<TimeSpan>(name: "NightLunchWindowEnd", table: "SystemConfiguration", type: "interval", nullable: false, defaultValue: new TimeSpan(4, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NightLunchWindowEnd", table: "SystemConfiguration");
            migrationBuilder.DropColumn(name: "NightLunchWindowStart", table: "SystemConfiguration");
        }
    }
}
