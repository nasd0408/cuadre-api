using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberShop.Migrations
{
    /// <inheritdoc />
    public partial class AddOfflineSyncIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OfflineId",
                table: "Citas",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Citas_EmpresaId_OfflineId",
                table: "Citas",
                columns: new[] { "EmpresaId", "OfflineId" },
                unique: true,
                filter: "\"OfflineId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Citas_EmpresaId_OfflineId",
                table: "Citas");

            migrationBuilder.DropColumn(
                name: "OfflineId",
                table: "Citas");
        }
    }
}
