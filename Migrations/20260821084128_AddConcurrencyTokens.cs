using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BarberShop.Migrations
{
    /// <summary>
    /// Concurrencia optimista sobre las entidades de dinero. NO LLEVA DDL A PROPOSITO.
    ///
    /// EF genero aqui cuatro `AddColumn<uint>(name: "xmin", ...)`. Ejecutarlos
    /// habria roto el despliegue: `xmin` es una COLUMNA DE SISTEMA de PostgreSQL
    /// y `ALTER TABLE ... ADD COLUMN xmin` falla con
    ///     "column name xmin conflicts with a system column name".
    ///
    /// La columna ya existe en toda tabla de PostgreSQL y guarda el id de
    /// transaccion que escribio la fila por ultima vez. Lo unico que hace falta es
    /// que EF la conozca, y eso vive en el modelo (AppDbContext.OnModelCreating),
    /// no en el esquema. Por eso esta migracion solo sirve para dejar constancia y
    /// mantener sincronizado el snapshot.
    ///
    /// Efecto real: EF anade "AND xmin = @original" a los UPDATE de Citas,
    /// CuentasCliente, PagosNomina y PagoSuscripcion. El segundo de dos cobros
    /// simultaneos afecta 0 filas, lanza DbUpdateConcurrencyException y revierte
    /// tambien el movimiento que iba en el mismo SaveChanges.
    /// </summary>
    public partial class AddConcurrencyTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intencionadamente vacia. Ver el comentario de la clase.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intencionadamente vacia: no se creo nada que revertir.
        }
    }
}
