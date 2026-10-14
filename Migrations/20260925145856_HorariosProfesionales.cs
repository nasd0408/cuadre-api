using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class HorariosProfesionales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HorarioAgendaActivo",
                table: "Empleados",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ZonaHorariaAgenda",
                table: "Empleados",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TramosHorario",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpleadoId = table.Column<int>(type: "integer", nullable: false),
                    DiaSemana = table.Column<int>(type: "integer", nullable: false),
                    InicioMinuto = table.Column<int>(type: "integer", nullable: false),
                    FinMinuto = table.Column<int>(type: "integer", nullable: false),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TramosHorario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TramosHorario_Empleados_EmpleadoId",
                        column: x => x.EmpleadoId,
                        principalTable: "Empleados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TramosHorario_EmpleadoId",
                table: "TramosHorario",
                column: "EmpleadoId");

            migrationBuilder.CreateIndex(
                name: "IX_TramosHorario_EmpresaId_EmpleadoId_DiaSemana",
                table: "TramosHorario",
                columns: new[] { "EmpresaId", "EmpleadoId", "DiaSemana" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TramosHorario");

            migrationBuilder.DropColumn(
                name: "HorarioAgendaActivo",
                table: "Empleados");

            migrationBuilder.DropColumn(
                name: "ZonaHorariaAgenda",
                table: "Empleados");
        }
    }
}
