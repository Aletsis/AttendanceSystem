using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AttendanceSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Huellas de los empleados externos, copiadas del reloj local para poder reenviarlas. Se borran con el empleado.
    /// </summary>
    public partial class AddExternalEmployeeFingerprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalEmployeeFingerprints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FingerIndex = table.Column<int>(type: "integer", nullable: false),
                    Template = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    ExternalEmployeeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalEmployeeFingerprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalEmployeeFingerprints_ExternalEmployees_ExternalEmpl~",
                        column: x => x.ExternalEmployeeId,
                        principalTable: "ExternalEmployees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalEmployeeFingerprints_ExternalEmployeeId",
                table: "ExternalEmployeeFingerprints",
                column: "ExternalEmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalEmployeeFingerprints");
        }
    }
}
