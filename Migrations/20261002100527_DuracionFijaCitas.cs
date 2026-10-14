using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class DuracionFijaCitas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuracionMinutos",
                table: "CitasServicios",
                type: "integer",
                nullable: true);

            // Para las reservas anteriores solo conocemos la duración actual del catálogo.
            migrationBuilder.Sql("""
                UPDATE "CitasServicios" AS cs
                SET "DuracionMinutos" = s."DuracionMinutos"
                FROM "Servicios" AS s
                WHERE cs."ServicioId" = s."Id" AND cs."DuracionMinutos" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuracionMinutos",
                table: "CitasServicios");
        }
    }
}
