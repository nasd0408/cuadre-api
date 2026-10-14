using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicesEmpresaId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Servicios_EmpresaId",
                table: "Servicios",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Recordatorios_EmpresaId_ClienteId",
                table: "Recordatorios",
                columns: new[] { "EmpresaId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_PagoSuscripcion_EmpresaId",
                table: "PagoSuscripcion",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_PagosNomina_EmpresaId",
                table: "PagosNomina",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_Estado",
                table: "Empresas",
                column: "Estado");

            migrationBuilder.CreateIndex(
                name: "IX_CuentasCliente_EmpresaId",
                table: "CuentasCliente",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_EmpresaId",
                table: "Clientes",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_CitasServicios_EmpresaId",
                table: "CitasServicios",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_EmpresaId_Estado",
                table: "Citas",
                columns: new[] { "EmpresaId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Citas_EmpresaId_Fecha",
                table: "Citas",
                columns: new[] { "EmpresaId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_AbonosPago_EmpresaId",
                table: "AbonosPago",
                column: "EmpresaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Servicios_EmpresaId",
                table: "Servicios");

            migrationBuilder.DropIndex(
                name: "IX_Recordatorios_EmpresaId_ClienteId",
                table: "Recordatorios");

            migrationBuilder.DropIndex(
                name: "IX_PagoSuscripcion_EmpresaId",
                table: "PagoSuscripcion");

            migrationBuilder.DropIndex(
                name: "IX_PagosNomina_EmpresaId",
                table: "PagosNomina");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_Estado",
                table: "Empresas");

            migrationBuilder.DropIndex(
                name: "IX_CuentasCliente_EmpresaId",
                table: "CuentasCliente");

            migrationBuilder.DropIndex(
                name: "IX_Clientes_EmpresaId",
                table: "Clientes");

            migrationBuilder.DropIndex(
                name: "IX_CitasServicios_EmpresaId",
                table: "CitasServicios");

            migrationBuilder.DropIndex(
                name: "IX_Citas_EmpresaId_Estado",
                table: "Citas");

            migrationBuilder.DropIndex(
                name: "IX_Citas_EmpresaId_Fecha",
                table: "Citas");

            migrationBuilder.DropIndex(
                name: "IX_AbonosPago_EmpresaId",
                table: "AbonosPago");
        }
    }
}
