using BarberShop.Data;
using BarberShop.Enum;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Services
{
    /// <summary>
    /// Arma el resumen diario ("Buenos dias: X clientes por cobrar, Y citas hoy...")
    /// y lo manda como push a los administradores de cada empresa.
    /// </summary>
    public interface INotificationDigestService
    {
        /// <summary>Compone y envía el digest diario a todas las empresas con dispositivos.</summary>
        Task EnviarDigestsAsync(CancellationToken ct = default);

        /// <summary>Envía el digest a una empresa (para pruebas). Devuelve el texto enviado o null.</summary>
        Task<string?> EnviarDigestEmpresaAsync(int empresaId, CancellationToken ct = default);
    }

    /// <summary>
    /// Implementacion del digest. Como corre desde un BackgroundService (sin
    /// usuario logueado), el filtro de tenant no sabe que empresa usar: por eso
    /// todas las consultas llevan .IgnoreQueryFilters() y filtran por EmpresaId
    /// a mano. Es la unica forma segura de leer datos de una empresa concreta
    /// fuera de una peticion HTTP.
    /// </summary>
    public class NotificationDigestService : INotificationDigestService
    {
        // Venezuela = UTC-4, sin horario de verano.
        private const int OffsetHorasVenezuela = 4;

        private readonly AppDbContext _db;
        private readonly IPushNotificationService _pushService;
        private readonly ILogger<NotificationDigestService> _logger;

        /// <summary>Dependencias inyectadas: BD, servicio de push y logger.</summary>
        public NotificationDigestService(
            AppDbContext db,
            IPushNotificationService pushService,
            ILogger<NotificationDigestService> logger)
        {
            _db = db;
            _pushService = pushService;
            _logger = logger;
        }

        /// <summary>
        /// Recorre todas las empresas que tienen algun dispositivo push y envia el
        /// digest de cada una. El try/catch por empresa evita que el fallo de una
        /// deje sin resumen a todas las demas.
        /// </summary>
        public async Task EnviarDigestsAsync(CancellationToken ct = default)
        {
            // Empresas que no están suspendidas y tienen al menos un dispositivo registrado
            var empresaIds = await _db.DispositivosPush
                .Select(d => d.EmpresaId)
                .Distinct()
                .ToListAsync(ct);

            foreach (var empresaId in empresaIds)
            {
                try
                {
                    await EnviarDigestEmpresaAsync(empresaId, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enviando digest a empresa {EmpresaId}", empresaId);
                }
            }
        }

        /// <summary>
        /// Calcula los indicadores de UNA empresa (deudores, recordatorios, citas
        /// de hoy, nomina vencida, resumen de ayer), compone el texto y lo envia.
        /// Devuelve null si la empresa esta suspendida, desactivo el digest o no
        /// hay nada que contar. Las fechas se calculan en hora de Venezuela
        /// (UTC-4) y se convierten a UTC para comparar con la BD.
        /// </summary>
        public async Task<string?> EnviarDigestEmpresaAsync(int empresaId, CancellationToken ct = default)
        {
            var empresa = await _db.Empresas
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

            if (empresa == null || empresa.Estado == EstadoSuscripcion.Suspendido)
                return null;

            var prefs = await _pushService.GetPreferenciasAsync(empresaId, ct);
            if (!prefs.DigestDiario) return null;

            // ── Deudores (saldo positivo por cliente) ──
            //
            // Traia a memoria el historico COMPLETO de movimientos de la empresa
            // para sumar dos numeros, y esto corre a diario por cada empresa. Es
            // el mismo patron que se arreglo en el dashboard, asi que comparte la
            // consulta: agrega en SQL y solo viajan los deudores.
            var saldos = await ConsultasClientela
                .SaldosDeudoresDe(_db, empresaId)
                .Select(s => s.Saldo)
                .ToListAsync(ct);

            var deudores = saldos.Count;
            var totalDeuda = saldos.Sum();

            // ── Recordatorios pendientes ──
            var recordatorios = await _db.Recordatorios
                .IgnoreQueryFilters()
                .CountAsync(r => r.EmpresaId == empresaId &&
                    (r.Estado == EstadoRecordatorio.Pendiente || r.Estado == EstadoRecordatorio.NoContestó), ct);

            // ── Resumen de ayer (día local Venezuela) ──
            var nowUtc = DateTime.UtcNow;
            var hoyLocal = nowUtc.AddHours(-OffsetHorasVenezuela).Date;
            var hoyInicioUtc = DateTime.SpecifyKind(
                hoyLocal.AddHours(OffsetHorasVenezuela), DateTimeKind.Unspecified);
            var mananaInicioUtc = hoyInicioUtc.AddDays(1);
            var ayerInicioUtc = DateTime.SpecifyKind(
                hoyLocal.AddDays(-1).AddHours(OffsetHorasVenezuela), DateTimeKind.Unspecified);
            var ayerFinUtc = DateTime.SpecifyKind(
                hoyLocal.AddHours(OffsetHorasVenezuela), DateTimeKind.Unspecified);

            var citasAyer = await _db.Citas
                .IgnoreQueryFilters()
                .Where(c => c.EmpresaId == empresaId &&
                            c.Estado == EstadoCita.Cerrada &&
                            c.Fecha >= ayerInicioUtc && c.Fecha < ayerFinUtc)
                .Select(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad))
                .ToListAsync(ct);

            var serviciosAyer = citasAyer.Count;
            var ingresosAyer = citasAyer.Sum();

            // ── Citas de hoy ──
            var citasHoy = await _db.Citas
                .IgnoreQueryFilters()
                .CountAsync(c => c.EmpresaId == empresaId &&
                    c.Estado != EstadoCita.Cancelada &&
                    c.Fecha >= hoyInicioUtc &&
                    c.Fecha < mananaInicioUtc, ct);

            // ── Nómina por pagar (corte vencido según periodicidad) ──
            var (empleadosNomina, comisionesNomina) = prefs.AvisoNomina
                ? await CalcularNominaVencidaAsync(empresaId, hoyLocal, ct)
                : (0, 0m);

            // ── Componer mensaje ──
            var lineas = new List<string>();
            if (deudores > 0)
                lineas.Add($"💰 {deudores} cliente{(deudores == 1 ? "" : "s")} por cobrar (${totalDeuda:N0}).");
            if (recordatorios > 0)
                lineas.Add($"📅 {recordatorios} cliente{(recordatorios == 1 ? "" : "s")} por recordar hoy.");
            if (citasHoy > 0)
                lineas.Add($"🗓️ {citasHoy} cita{(citasHoy == 1 ? "" : "s")} en agenda hoy.");
            if (empleadosNomina > 0)
                lineas.Add($"✂️ Nómina lista para generar: {empleadosNomina} empleado{(empleadosNomina == 1 ? "" : "s")} (${comisionesNomina:N0}).");
            if (serviciosAyer > 0)
                lineas.Add($"Ayer: {serviciosAyer} servicio{(serviciosAyer == 1 ? "" : "s")}, ${ingresosAyer:N0}.");

            // Nada relevante que decir: no molestamos
            if (lineas.Count == 0) return null;

            var pantalla = deudores > 0 ? "Deudores"
                         : recordatorios > 0 ? "Recordatorios"
                         : empleadosNomina > 0 ? "Nominas"
                         : citasHoy > 0 ? "Citas"
                         : "Dashboard";

            var title = "Buenos días 👋";
            var body = string.Join(" ", lineas);
            if (deudores > 0 || recordatorios > 0)
                body += " Toca para gestionar.";

            var dispositivos = await _pushService.SendToEmpresaAsync(
                empresaId, title, body, pantalla, soloAdministradores: true, ct);

            if (dispositivos == 0) return null;

            _logger.LogInformation(
                "Digest enviado a empresa {EmpresaId} ({Dispositivos} disp.): {Body}",
                empresaId, dispositivos, body);

            return body;
        }

        /// <summary>
        /// Empleados activos con comisiones sin procesar cuyo corte venció según su
        /// periodicidad (Semanal=7, Quincenal=15, Mensual=30 días). Si nunca se les ha
        /// pagado, se cuenta desde su cita cerrada sin pagar más antigua.
        /// </summary>
        private async Task<(int empleados, decimal comisiones)> CalcularNominaVencidaAsync(
            int empresaId, DateTime hoyLocal, CancellationToken ct)
        {
            var empleados = await _db.Empleados
                .IgnoreQueryFilters()
                .Where(e => e.EmpresaId == empresaId && e.Activo)
                .Select(e => new { e.Id, e.PorcentajeComision, e.Periodicidad, e.FechaUltimoPago })
                .ToListAsync(ct);

            if (empleados.Count == 0) return (0, 0m);

            // Citas cerradas sin pagar al empleado, con su total y fecha
            var citasSinPagar = await _db.Citas
                .IgnoreQueryFilters()
                .Where(c => c.EmpresaId == empresaId &&
                            c.Estado == EstadoCita.Cerrada &&
                            !c.PagadoAlEmpleado)
                .Select(c => new
                {
                    c.EmpleadoId,
                    c.Fecha,
                    Total = c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad),
                })
                .ToListAsync(ct);

            if (citasSinPagar.Count == 0) return (0, 0m);

            var vencidos = 0;
            var comisiones = 0m;

            foreach (var emp in empleados)
            {
                var pendientes = citasSinPagar.Where(c => c.EmpleadoId == emp.Id).ToList();
                if (pendientes.Count == 0) continue;

                var dias = emp.Periodicidad switch
                {
                    PeriodicidadPago.Semanal => 7,
                    PeriodicidadPago.Quincenal => 15,
                    _ => 30,
                };

                // Referencia (UTC): último pago, o la cita pendiente más antigua
                var referenciaUtc = emp.FechaUltimoPago ?? pendientes.Min(c => c.Fecha);
                var referenciaLocal = referenciaUtc.AddHours(-OffsetHorasVenezuela).Date;

                if (referenciaLocal.AddDays(dias) > hoyLocal) continue; // corte aún no vence

                vencidos++;
                comisiones += pendientes.Sum(c => c.Total) * (emp.PorcentajeComision / 100m);
            }

            return (vencidos, comisiones);
        }
    }
}
