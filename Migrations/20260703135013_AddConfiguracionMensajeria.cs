using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class AddConfiguracionMensajeria : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionesMensajeria",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CodigoPais = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    AppDestino = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlantillaRecordatorio = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PlantillaDeudor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PlantillaConfirmacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmpresaId = table.Column<int>(type: "integer", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionesMensajeria", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracionesMensajeria_EmpresaId",
                table: "ConfiguracionesMensajeria",
                column: "EmpresaId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionesMensajeria");
        }
    }
}
