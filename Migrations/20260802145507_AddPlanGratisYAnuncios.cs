using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanGratisYAnuncios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue = true (editado a mano): EF no toma el inicializador de la
            // propiedad, y con false las empresas de pago existentes perderían el acceso
            // web al aplicar la migración. Los planes nuevos setean el valor explícitamente
            // desde PlanDefaults, así que el default solo afecta al backfill de filas viejas.
            migrationBuilder.AddColumn<bool>(
                name: "AccesoWeb",
                table: "EmpresaConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Aquí false sí es lo correcto: ninguna empresa existente debe empezar a ver anuncios.
            migrationBuilder.AddColumn<bool>(
                name: "MostrarAnuncios",
                table: "EmpresaConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccesoWeb",
                table: "EmpresaConfigs");

            migrationBuilder.DropColumn(
                name: "MostrarAnuncios",
                table: "EmpresaConfigs");
        }
    }
}
