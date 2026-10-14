using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Interfaces;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Reportes del modulo de pago "Reportes": deudores e historial de ingresos
/// (bruto, nomina pagada y neto) con clientes nuevos y en riesgo.
/// </summary>
[ApiController]
[Route("api/reportes")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class ReportesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserContext _userContext;
    private readonly PlanValidationService _planService;

    /// <summary>Recibe BD, contexto del usuario y validador de planes por DI.</summary>
    public ReportesController(AppDbContext context, IUserContext userContext, PlanValidationService planService)
    {
        _context = context;
        _userContext = userContext;
        _planService = planService;
    }
    // =========================================================
    // GET: api/reportes/deudores
    // Lista de clientes con deuda
    // =========================================================
    /// <summary>Clientes con saldo pendiente, calculado a partir de sus movimientos y citas por pagar.</summary>
    [HttpGet("deudores")]
    public async Task<ActionResult<ReporteDeudoresDto>> GetDeudores()
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return Unauthorized();

        // Obtener todos los movimientos
        var movimientos = await _context.CuentasCliente
            .Include(c => c.Cliente)
            .ToListAsync();

        // Obtener citas pendientes de pago
        var citasPendientes = await _context.Citas
            .Include(c => c.Cliente)
            .Include(c => c.CitaServicios)
                .ThenInclude(cs => cs.Servicio)
            .Where(c => c.Estado == EstadoCita.PagoPendiente)
            .ToListAsync();

        // Calcular saldo por cliente
        var saldosPorCliente = movimientos
            .GroupBy(m => m.ClienteId)
            .Select(g => new
            {
                ClienteId = g.Key,
                Cliente = g.First().Cliente,
                Saldo = g.Sum(m => m.Tipo == TipoMovimiento.Cargo ? m.Monto : -m.Monto),
                Movimientos = g.ToList()
            })
            .Where(s => s.Saldo > 0)
            .ToList();

        var deudores = new List<ClienteDeudorDto>();

        foreach (var saldo in saldosPorCliente)
        {
            var citasCliente = citasPendientes
                .Where(c => c.ClienteId == saldo.ClienteId)
                .ToList();

            var detalle = citasCliente.Select(c => new DeudaDetalleDto
            {
                CitaId = c.Id,
                Fecha = c.Fecha,
                Monto = c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad),
                Servicios = string.Join(", ", c.CitaServicios.Select(cs => cs.Servicio?.Nombre ?? "Servicio"))
            }).ToList();

            var primeraDeuda = saldo.Movimientos
                .Where(m => m.Tipo == TipoMovimiento.Cargo)
                .OrderBy(m => m.FechaCreacion)
                .FirstOrDefault();

            deudores.Add(new ClienteDeudorDto
            {
                ClienteId = saldo.ClienteId,
                Nombre = saldo.Cliente?.Nombre ?? "Sin nombre",
                Telefono = saldo.Cliente?.Telefono,
                Deuda = saldo.Saldo,
                FechaPrimeraDeuda = primeraDeuda?.FechaCreacion,
                FechaUltimaVisita = saldo.Cliente?.FechaUltimoCorte,
                CitasPendientes = citasCliente.Count,
                Detalle = detalle
            });
        }

        var ordenados = deudores.OrderByDescending(d => d.Deuda).ToList();

        return Ok(new ReporteDeudoresDto
        {
            TotalDeuda = ordenados.Sum(d => d.Deuda),
            TotalDeudores = ordenados.Count,
            DeudaPromedio = ordenados.Count > 0 ? ordenados.Average(d => d.Deuda) : 0,
            Deudores = ordenados
        });
    }

    // =========================================================
    // GET: api/reportes/historial
    // Historial de ingresos con filtros + nóminas (neto)
    // =========================================================
    /// <summary>
    /// Historial de ingresos en un rango de fechas agrupado por dia/semana/mes.
    /// Recorta la fecha inicial segun la retencion del plan (el plan Basico solo
    /// ve 3 meses, por ejemplo), compara con el periodo anterior y calcula
    /// clientes nuevos y en riesgo con las consultas de ConsultasClientela.
    /// </summary>
    [HttpGet("historial")]
    public async Task<ActionResult<ReporteHistorialDto>> GetHistorial([FromQuery] FiltrosHistorialDto filtros)
    {
        var hoy = DateTime.Today;
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return Unauthorized();

        // ── Validar módulo ──
        var (permitido, error) = await _planService.TieneModuloAsync(empresaId.Value, "reportes");
        if (!permitido)
            return StatusCode(403, new { message = error, code = "MODULE_DISABLED" });

        // ── Aplicar retención de historial ──
        var fechaMin = await _planService.GetFechaMinHistorialAsync(empresaId.Value);
        if (fechaMin.HasValue && filtros.FechaDesde.HasValue && filtros.FechaDesde < fechaMin)
        {
            filtros.FechaDesde = fechaMin;
        }
        // Determinar rango de fechas
        var fechaHasta = filtros.FechaHasta?.Date.AddDays(1) ?? hoy.AddDays(1);
        var fechaDesde = filtros.FechaDesde?.Date ?? hoy.AddDays(-30);

        // Tope al rango. GenerarPeriodos recorre las listas COMPLETAS de citas y
        // abonos una vez por periodo, asi que con agrupacion diaria un
        // ?fechaDesde=1900-01-01 son ~46.000 iteraciones sobre todo el historico:
        // un DoS con un parametro de query, sin autenticacion privilegiada.
        //
        // Dos anos cubre cualquier comparativa real de un negocio de barrio.
        const int MAX_DIAS_RANGO = 366 * 2;
        if ((fechaHasta - fechaDesde).TotalDays > MAX_DIAS_RANGO)
            fechaDesde = fechaHasta.AddDays(-MAX_DIAS_RANGO);

        if (fechaDesde > fechaHasta)
            return BadRequest("La fecha de inicio no puede ser posterior a la de fin.");

        // Calcular período anterior para comparación
        var duracion = (fechaHasta - fechaDesde).Days;
        var fechaDesdeAnterior = fechaDesde.AddDays(-duracion);
        var fechaHastaAnterior = fechaDesde;

        // ── Citas del período ──
        var query = _context.Citas
            .Include(c => c.CitaServicios)
                .ThenInclude(cs => cs.Servicio)
            .Include(c => c.Empleado)
            .Where(c => c.Fecha >= fechaDesde && c.Fecha < fechaHasta && c.Estado == EstadoCita.Cerrada);

        if (filtros.EmpleadoId.HasValue)
            query = query.Where(c => c.EmpleadoId == filtros.EmpleadoId);

        var citas = await query.ToListAsync();

        // Citas del período anterior
        var citasAnterior = await _context.Citas
            .Include(c => c.CitaServicios)
            .Where(c => c.Fecha >= fechaDesdeAnterior && c.Fecha < fechaHastaAnterior && c.Estado == EstadoCita.Cerrada)
            .ToListAsync();

        // ── Abonos de nómina del período (dinero que salió) ──
        var abonosQuery = _context.AbonosPago
            .Include(a => a.PagoNomina)
                .ThenInclude(p => p.Empleado)
            .Where(a => a.FechaAbono >= fechaDesde && a.FechaAbono < fechaHasta);

        if (filtros.EmpleadoId.HasValue)
            abonosQuery = abonosQuery.Where(a => a.PagoNomina.EmpleadoId == filtros.EmpleadoId);

        var abonos = await abonosQuery.ToListAsync();

        var abonosAnterior = await _context.AbonosPago
            .Where(a => a.FechaAbono >= fechaDesdeAnterior && a.FechaAbono < fechaHastaAnterior)
            .ToListAsync();

        // ── Calcular totales ──
        var ingresosTotales = citas.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));
        var serviciosTotales = citas.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));
        var citasTotales = citas.Count;

        var ingresosAnterior = citasAnterior.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));
        var porcentajeCambio = ingresosAnterior > 0
            ? ((ingresosTotales - ingresosAnterior) / ingresosAnterior) * 100
            : (ingresosTotales > 0 ? 100 : 0);

        // Totales nómina
        var totalNominas = abonos.Sum(a => a.Monto);
        var totalNominasAnterior = abonosAnterior.Sum(a => a.Monto);

        // Neto
        var ingresoNeto = ingresosTotales - totalNominas;
        var ingresoNetoAnterior = ingresosAnterior - totalNominasAnterior;
        var porcentajeCambioNeto = ingresoNetoAnterior > 0
            ? ((ingresoNeto - ingresoNetoAnterior) / ingresoNetoAnterior) * 100
            : (ingresoNeto > 0 ? 100 : 0);

        // ── Generar períodos (con nóminas) ──
        var periodos = GenerarPeriodos(filtros.Agrupacion ?? "dia", fechaDesde, fechaHasta, citas, abonos);

        // ── Top empleados ──
        var empleados = await _context.Empleados.Where(e => e.Activo).ToListAsync();
        var topEmpleados = empleados
            .Select(e =>
            {
                var citasEmpleado = citas.Where(c => c.EmpleadoId == e.Id).ToList();
                var total = citasEmpleado.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));
                return new RendimientoBarberoDto
                {
                    BarberoId = e.Id,
                    Nombre = e.Nombre,
                    ServiciosCount = citasEmpleado.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad)),
                    TotalGenerado = total,
                    ComisionGenerada = total * (e.PorcentajeComision / 100),
                    PorcentajeComision = e.PorcentajeComision
                };
            })
            .Where(e => e.ServiciosCount > 0)
            .OrderByDescending(e => e.TotalGenerado)
            .Take(5)
            .ToList();

        // ── Top servicios ──
        var topServicios = citas
            .SelectMany(c => c.CitaServicios)
            .GroupBy(cs => new { cs.ServicioId, Nombre = cs.Servicio?.Nombre ?? "Servicio" })
            .Select(g => new TopServicioDto
            {
                ServicioId = g.Key.ServicioId,
                Nombre = g.Key.Nombre,
                Cantidad = g.Sum(x => x.Cantidad),
                Ingresos = g.Sum(x => x.Precio * x.Cantidad)
            })
            .OrderByDescending(x => x.Cantidad)
            .Take(5)
            .ToList();

        // ── Desglose de nóminas por empleado en el período ──
        var desglosePagos = abonos
            .GroupBy(a => new { a.PagoNomina.EmpleadoId, Nombre = a.PagoNomina.Empleado?.Nombre ?? "Empleado" })
            .Select(g => new DesglosePagoEmpleadoDto
            {
                EmpleadoId = g.Key.EmpleadoId,
                EmpleadoNombre = g.Key.Nombre,
                TotalPagado = g.Sum(a => a.Monto),
                CantidadAbonos = g.Count()
            })
            .OrderByDescending(x => x.TotalPagado)
            .ToList();

        // ── Clientela ──
        // Un cliente es "nuevo" si su primera cita cerrada cae dentro del período.
        // Se compara contra TODO su historial, no solo contra el período anterior:
        // alguien que vino hace un año y volvió ahora es recurrente, no nuevo.
        const int DIAS_PARA_RIESGO = 60;

        var clientesDelPeriodo = citas.Select(c => c.ClienteId).Distinct().ToList();

        var primeraVisitaPorCliente = await ConsultasClientela
            .PrimeraVisitaDe(_context, clientesDelPeriodo)
            .ToListAsync();

        var clientesNuevos = primeraVisitaPorCliente
            .Count(x => x.Primera >= fechaDesde && x.Primera < fechaHasta);

        var atendidos = clientesDelPeriodo.Count;
        var recurrentes = atendidos - clientesNuevos;

        // En riesgo: ya fueron clientes alguna vez, pero llevan demasiado sin volver.
        // Se mide contra hoy, no contra el período: es un estado actual del negocio.
        // Se filtra con Where sobre el grupo (se traduce a HAVING) en vez de proyectar
        // y luego contar: es la forma que EF traduce de manera fiable a SQL. Un reporte
        // que revienta en producción por una traducción rara no compensa el atajo.
        var limiteRiesgo = hoy.AddDays(-DIAS_PARA_RIESGO);
        var enRiesgo = await ConsultasClientela
            .ClientesEnRiesgo(_context, limiteRiesgo)
            .CountAsync();

        return Ok(new ReporteHistorialDto
        {
            FechaDesde = fechaDesde,
            FechaHasta = fechaHasta.AddDays(-1),
            Clientela = new ClientelaDto
            {
                Atendidos = atendidos,
                Nuevos = clientesNuevos,
                Recurrentes = recurrentes,
                PorcentajeRecurrencia = atendidos > 0
                    ? Math.Round((decimal)recurrentes / atendidos * 100, 1)
                    : 0,
                EnRiesgo = enRiesgo,
                DiasParaRiesgo = DIAS_PARA_RIESGO,
            },
            Agrupacion = filtros.Agrupacion ?? "dia",
            // Bruto
            IngresosTotales = ingresosTotales,
            ServiciosTotales = serviciosTotales,
            CitasTotales = citasTotales,
            TicketPromedio = citasTotales > 0 ? ingresosTotales / citasTotales : 0,
            IngresosPeriodoAnterior = ingresosAnterior,
            PorcentajeCambio = porcentajeCambio,
            // Nóminas
            TotalNominas = totalNominas,
            NominasPeriodoAnterior = totalNominasAnterior,
            // Neto
            IngresoNeto = ingresoNeto,
            PorcentajeCambioNeto = porcentajeCambioNeto,
            // Detalle
            Periodos = periodos,
            TopEmpleados = topEmpleados,
            TopServicios = topServicios,
            DesglosePagos = desglosePagos
        });
    }
    // =========================================================
    // Helper: Generar períodos según agrupación
    // =========================================================
    /// <summary>
    /// Parte el rango en periodos consecutivos (dias, semanas o meses) y crea un
    /// resumen para cada uno filtrando en memoria las citas y abonos de nomina
    /// que caen dentro.
    /// </summary>
    private List<ResumenPeriodoDto> GenerarPeriodos(
        string agrupacion,
        DateTime fechaDesde,
        DateTime fechaHasta,
        List<BarberShop.Models.Cita> citas,
        List<BarberShop.Models.AbonoPago> abonos)  
    {
        var periodos = new List<ResumenPeriodoDto>();
        switch (agrupacion.ToLower())
        {
            case "dia":
                var fecha = fechaDesde;
                while (fecha < fechaHasta)
                {
                    var finDia = fecha.AddDays(1);
                    var citasDia = citas.Where(c => c.Fecha >= fecha && c.Fecha < finDia).ToList();
                    var abonosDia = abonos.Where(a => a.FechaAbono >= fecha && a.FechaAbono < finDia).ToList();
                    periodos.Add(CrearResumenPeriodo(
                        fecha.ToString("dd/MM"),
                        fecha,
                        finDia,
                        citasDia,
                        abonosDia
                    ));
                    fecha = finDia;
                }
                break;
            case "semana":
                var semana = fechaDesde;
                var numSemana = 1;
                while (semana < fechaHasta)
                {
                    var finSemana = semana.AddDays(7);
                    if (finSemana > fechaHasta) finSemana = fechaHasta;
                    var citasSemana = citas.Where(c => c.Fecha >= semana && c.Fecha < finSemana).ToList();
                    var abonosSemana = abonos.Where(a => a.FechaAbono >= semana && a.FechaAbono < finSemana).ToList();
                    periodos.Add(CrearResumenPeriodo(
                        $"Sem {numSemana}",
                        semana,
                        finSemana,
                        citasSemana,
                        abonosSemana
                    ));
                    semana = finSemana;
                    numSemana++;
                }
                break;
            case "mes":
                var mes = new DateTime(fechaDesde.Year, fechaDesde.Month, 1);
                while (mes < fechaHasta)
                {
                    var finMes = mes.AddMonths(1);
                    if (finMes > fechaHasta) finMes = fechaHasta;
                    var citasMes = citas.Where(c => c.Fecha >= mes && c.Fecha < finMes).ToList();
                    var abonosMes = abonos.Where(a => a.FechaAbono >= mes && a.FechaAbono < finMes).ToList();
                    var nombreMes = mes.ToString("MMM yyyy", new System.Globalization.CultureInfo("es-MX"));
                    periodos.Add(CrearResumenPeriodo(
                        nombreMes,
                        mes,
                        finMes,
                        citasMes,
                        abonosMes
                    ));
                    mes = finMes;
                }
                break;
        }
        return periodos;
    }


    /// <summary>Totales de un periodo: ingresos, servicios, citas, ticket promedio, nomina pagada y neto.</summary>
    private ResumenPeriodoDto CrearResumenPeriodo(
     string etiqueta,
     DateTime inicio,
     DateTime fin,
     List<BarberShop.Models.Cita> citas,
     List<BarberShop.Models.AbonoPago> abonos) 
    {
        var ingresos = citas.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));
        var servicios = citas.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));
        var nominas = abonos.Sum(a => a.Monto);

        return new ResumenPeriodoDto
        {
            Etiqueta = etiqueta,
            FechaInicio = inicio,
            FechaFin = fin,
            Ingresos = ingresos,
            Servicios = servicios,
            Citas = citas.Count,
            TicketPromedio = citas.Count > 0 ? ingresos / citas.Count : 0,
            Nominas = nominas,              
            Neto = ingresos - nominas       
        };
    }
}
