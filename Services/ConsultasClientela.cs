using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Services
{
    // Un "record" es una clase inmutable con igualdad por valor, ideal para
    // resultados de consultas. Esta sintaxis de una linea genera constructor,
    // propiedades de solo lectura, Equals y ToString automaticamente.

    /// <summary>Cliente y fecha de su primera visita cerrada.</summary>
    public record PrimeraVisitaCliente(int ClienteId, DateTime Primera);

    /// <summary>Cliente y cuanto debe (cargos - abonos).</summary>
    public record SaldoCliente(int ClienteId, decimal Saldo);

    /// <summary>
    /// Consultas de clientela del reporte de historial.
    ///
    /// Viven fuera del controlador para poder verificar que EF las traduce a SQL sin
    /// necesidad de base de datos ni de sesión: son agregaciones con GroupBy, que es
    /// justo donde EF puede quedarse sin traducción y tumbar el endpoint en producción.
    /// </summary>
    public static class ConsultasClientela
    {
        /// <summary>
        /// Clientes cuya última visita cerrada es anterior al límite: los que se están
        /// perdiendo. El filtro va sobre el grupo (se traduce a HAVING).
        /// </summary>
        public static IQueryable<int> ClientesEnRiesgo(AppDbContext db, DateTime limite) =>
            db.Citas
                .Where(c => c.Estado == EstadoCita.Cerrada)
                .GroupBy(c => c.ClienteId)
                .Where(g => g.Max(c => c.Fecha) < limite)
                .Select(g => g.Key);

        /// <summary>
        /// Fecha de la primera visita cerrada de cada cliente indicado. Sirve para
        /// distinguir a un cliente nuevo de uno que ya había venido antes.
        /// </summary>
        public static IQueryable<PrimeraVisitaCliente> PrimeraVisitaDe(
            AppDbContext db,
            IEnumerable<int> clienteIds) =>
            db.Citas
                .Where(c => c.Estado == EstadoCita.Cerrada && clienteIds.Contains(c.ClienteId))
                .GroupBy(c => c.ClienteId)
                .Select(g => new PrimeraVisitaCliente(g.Key, g.Min(c => c.Fecha)));

        /// <summary>
        /// Saldo por cliente, solo de los que DEBEN algo.
        ///
        /// El dashboard hacia `CuentasCliente.ToListAsync()` y agrupaba en C#:
        /// traia el historico completo de movimientos de la empresa en cada carga
        /// de la pantalla de inicio, solo para sumar dos numeros. Aqui el GROUP BY
        /// y el HAVING los hace Postgres y solo viajan los deudores.
        ///
        /// El condicional dentro del Sum se traduce a SUM(CASE WHEN ...).
        /// </summary>
        public static IQueryable<SaldoCliente> SaldosDeudores(AppDbContext db) =>
            Deudores(db.CuentasCliente.AsNoTracking());

        /// <summary>
        /// Misma agregacion, para el job de notificaciones, que corre sin sesion y
        /// tiene que saltarse el filtro de tenant acotando por empresa a mano.
        /// </summary>
        public static IQueryable<SaldoCliente> SaldosDeudoresDe(AppDbContext db, int empresaId) =>
            Deudores(db.CuentasCliente
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(m => m.EmpresaId == empresaId));

        /// <summary>
        /// Nucleo comun de las dos consultas de saldos. Devuelve un IQueryable, o
        /// sea una consulta SIN ejecutar: quien la llama puede seguir encadenando
        /// Where/OrderBy y el SQL solo se manda a Postgres al hacer ToListAsync().
        /// </summary>
        private static IQueryable<SaldoCliente> Deudores(IQueryable<CuentaCliente> origen) =>
            origen
                .GroupBy(m => m.ClienteId)
                // Dos sumas en vez de una con condicional negado: EF Core 10 no
                // traduce `Sum(m => cond ? m.Monto : -m.Monto)` dentro de la
                // proyeccion, lo envuelve en AsQueryable() y se rinde. Asi son
                // dos SUM(CASE WHEN ...) que si entiende.
                .Select(g => new
                {
                    ClienteId = g.Key,
                    Cargos = g.Sum(m => m.Tipo == TipoMovimiento.Cargo ? m.Monto : 0m),
                    Abonos = g.Sum(m => m.Tipo == TipoMovimiento.Abono ? m.Monto : 0m),
                })
                .Where(x => x.Cargos - x.Abonos > 0)
                .Select(x => new SaldoCliente(x.ClienteId, x.Cargos - x.Abonos));
    }
}
