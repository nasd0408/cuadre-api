using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class ReservasPublicas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReservasActivas",
                table: "PerfilesPublicos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SucursalReservasId",
                table: "PerfilesPublicos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenReservas",
                table: "PerfilesPublicos",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReservasPublicas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SolicitudHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CitaId = table.Column<int>(type: "integer", nullable: false),
                    NombreCliente = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    NombreLocal = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DireccionLocal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EmpresaId = table.Column<int>(type: "integer", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservasPublicas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReservasPublicas_Citas_CitaId",
                        column: x => x.CitaId,
                        principalTable: "Citas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerfilesPublicos_SucursalReservasId",
                table: "PerfilesPublicos",
                column: "SucursalReservasId");

            migrationBuilder.CreateIndex(
                name: "IX_PerfilesPublicos_TokenReservas",
                table: "PerfilesPublicos",
                column: "TokenReservas",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservasPublicas_CitaId",
                table: "ReservasPublicas",
                column: "CitaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservasPublicas_TokenHash",
                table: "ReservasPublicas",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PerfilesPublicos_Sucursales_SucursalReservasId",
                table: "PerfilesPublicos",
                column: "SucursalReservasId",
                principalTable: "Sucursales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PerfilesPublicos_Sucursales_SucursalReservasId",
                table: "PerfilesPublicos");

            migrationBuilder.DropTable(
                name: "ReservasPublicas");

            migrationBuilder.DropIndex(
                name: "IX_PerfilesPublicos_SucursalReservasId",
                table: "PerfilesPublicos");

            migrationBuilder.DropIndex(
                name: "IX_PerfilesPublicos_TokenReservas",
                table: "PerfilesPublicos");

            migrationBuilder.DropColumn(
                name: "ReservasActivas",
                table: "PerfilesPublicos");

            migrationBuilder.DropColumn(
                name: "SucursalReservasId",
                table: "PerfilesPublicos");

            migrationBuilder.DropColumn(
                name: "TokenReservas",
                table: "PerfilesPublicos");
        }
    }
}
