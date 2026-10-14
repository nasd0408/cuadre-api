using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Pantalla de inicio del dueno: ingresos, servicios, comparacion con el
/// periodo anterior, graficos, nomina pendiente, cuentas por cobrar,
/// rendimiento por barbero y top de servicios, todo en una sola llamada.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    /// <summary>Recibe el DbContext por DI.</summary>
    public DashboardController(AppDbContext context)
    {
        _context = context;
    }

    // =========================================================
    // GET: api/dashboard?periodo=dia|semana|mes
    // Dashboard completo con período configurable
    // =========================================================
    /// <summary>
    /// Arma el dashboard para el periodo pedido (dia, semana o mes). Trabaja con
    /// la zona horaria del usuario: calcula los limites del periodo en hora local
    /// y los convierte a UTC para filtrar las citas. Trae las citas del periodo y
    /// del periodo anterior y calcula las metricas en memoria.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DashboardCompleteDto>> GetDashboard(
        [FromQuery] string periodo = "dia",
        [FromQuery] string? fecha = null,
        [FromQuery] int? tzOffsetMinutes = null)
    {
        // Zona horaria del usuario: offset en minutos como lo da JS getTimezoneOffset()
        // (positivo al oeste de UTC; p.ej. Cuba/Venezuela UTC-4 => 240).
        // Si no viene (app vieja), offset 0 = comportamiento anterior basado en UTC.
        var offset = tzOffsetMinutes ?? 0;

        // "Hoy" según el dispositivo del usuario, no según el reloj UTC del servidor.
        var hoyLocal = DateTime.TryParse(fecha, out var fechaParsed)
            ? fechaParsed.Date
            : DateTime.UtcNow.AddMinutes(-offset).Date;

        DateTime inicioLocal, finLocal, inicioAnteriorLocal, finAnteriorLocal;
        string periodoNombre;

        // Calcular rangos (en hora local del usuario) según período
        switch (periodo.ToLower())
        {
            case "semana":
                var inicioSemana = hoyLocal.AddDays(-(int)hoyLocal.DayOfWeek);
                inicioLocal = inicioSemana;
                finLocal = hoyLocal.AddDays(1);
                inicioAnteriorLocal = inicioSemana.AddDays(-7);
                finAnteriorLocal = inicioSemana;
                periodoNombre = "Esta Semana";
                break;

            case "mes":
                inicioLocal = new DateTime(hoyLocal.Year, hoyLocal.Month, 1);
                finLocal = hoyLocal.AddDays(1);
                inicioAnteriorLocal = inicioLocal.AddMonths(-1);
                finAnteriorLocal = inicioLocal;
                periodoNombre = "Este Mes";
                break;

            default: // dia
                inicioLocal = hoyLocal;
                finLocal = hoyLocal.AddDays(1);
                inicioAnteriorLocal = hoyLocal.AddDays(-1);
                finAnteriorLocal = hoyLocal;
                periodoNombre = "Hoy";
                break;
        }

        // Convertir los límites locales al instante UTC equivalente para filtrar.
        // Las citas se guardan como instantes UTC; UTC = local + offset.
        DateTime ToUtc(DateTime local) =>
            DateTime.SpecifyKind(local.AddMinutes(offset), DateTimeKind.Unspecified);

        var fechaInicio = ToUtc(inicioLocal);
        var fechaFin = ToUtc(finLocal);
        var fechaInicioAnterior = ToUtc(inicioAnteriorLocal);
        var fechaFinAnterior = ToUtc(finAnteriorLocal);

        // Obtener todas las citas del período
        var citasPeriodo = await _context.Citas
            .Include(c => c.CitaServicios)
                .ThenInclude(cs => cs.Servicio)
            .Include(c => c.Empleado)
            .Where(c => c.Fecha >= fechaInicio && c.Fecha < fechaFin)
            .ToListAsync();

        // Citas del período anterior (para comparación)
        var citasAnterior = await _context.Citas
            .Include(c => c.CitaServicios)
            .Where(c => c.Fecha >= fechaInicioAnterior && c.Fecha < fechaFinAnterior)
            .ToListAsync();

        // Calcular métricas del período
        var citasCerradas = citasPeriodo.Where(c => c.Estado == EstadoCita.Cerrada).ToList();
        var ingresosTotales = citasCerradas.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));
        var totalServicios = citasCerradas.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));

        var metricasPeriodo = new MetricasPeriodoDto
        {
            IngresosTotales = ingresosTotales,
            TotalServicios = totalServicios,
            CitasCerradas = citasCerradas.Count,
            CitasPendientes = citasPeriodo.Count(c => c.Estado == EstadoCita.PagoPendiente || c.Estado == EstadoCita.Pendiente),
            CitasCanceladas = citasPeriodo.Count(c => c.Estado == EstadoCita.Cancelada),
            TicketPromedio = citasCerradas.Count > 0 ? ingresosTotales / citasCerradas.Count : 0
        };

        // Comparación con período anterior
        var citasCerradasAnterior = citasAnterior.Where(c => c.Estado == EstadoCita.Cerrada).ToList();
        var ingresosAnterior = citasCerradasAnterior.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));
        var serviciosAnterior = citasCerradasAnterior.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));

        var comparacion = new ComparacionDto
        {
            IngresosPeriodoAnterior = ingresosAnterior,
            ServiciosPeriodoAnterior = serviciosAnterior,
            PorcentajeCambioIngresos = ingresosAnterior > 0
                ? ((ingresosTotales - ingresosAnterior) / ingresosAnterior) * 100
                : (ingresosTotales > 0 ? 100 : 0),
            PorcentajeCambioServicios = serviciosAnterior > 0
                ? ((decimal)(totalServicios - serviciosAnterior) / serviciosAnterior) * 100
                : (totalServicios > 0 ? 100 : 0)
        };

        // Datos para gráficos (etiquetas y agrupación en hora local del usuario)
        var (graficoServicios, graficoIngresos) = GenerarDatosGrafico(
            periodo, inicioLocal, finLocal, hoyLocal, offset, citasPeriodo);

        // Resumen de nóminas
        var empleados = await _context.Empleados.Where(e => e.Activo).ToListAsync();
        var nominasPendientes = await _context.PagosNomina
            .Where(p => p.Estado != EstadoPago.Pagado)
            .ToListAsync();

        var citasSinProcesar = await _context.Citas
            .Include(c => c.CitaServicios)
            .Where(c => c.Estado == EstadoCita.Cerrada && !c.PagadoAlEmpleado)
            .ToListAsync();

        decimal comisionesSinProcesar = 0;
        foreach (var cita in citasSinProcesar)
        {
            var empleado = empleados.FirstOrDefault(e => e.Id == cita.EmpleadoId);
            if (empleado != null)
            {
                var totalCita = cita.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);
                comisionesSinProcesar += totalCita * (empleado.PorcentajeComision / 100);
            }
        }

        var resumenNominas = new ResumenNominasDto
        {
            TotalPendiente = nominasPendientes.Sum(n => n.MontoTotal - n.MontoPagado) + comisionesSinProcesar,
            EmpleadosConPendiente = citasSinProcesar.Select(c => c.EmpleadoId).Distinct().Count(),
            ComisionesSinProcesar = comisionesSinProcesar,
            CitasSinProcesar = citasSinProcesar.Count
        };

        // Cuentas por cobrar.
        //
        // Antes hacia `CuentasCliente.ToListAsync()` y agrupaba en C#: traia a
        // memoria el HISTORICO COMPLETO de movimientos de la empresa, en cada
        // carga del dashboard, solo para sumar dos numeros. Crece sin limite con
        // el uso, y es la pantalla que se abre al entrar.
        //
        // Ahora el GROUP BY y el HAVING los hace Postgres y solo viajan las
        // filas de clientes que deben algo.
        // Vive en ConsultasClientela para que un test compruebe que EF la traduce
        // a SQL: una agregacion que deja de traducirse tumba el endpoint en
        // produccion y aqui no hay try/catch que lo disimule.
        var saldosPorCliente = await ConsultasClientela.SaldosDeudores(_context).ToListAsync();

        var cuentasPorCobrar = saldosPorCliente.Sum(s => s.Saldo);
        var clientesDeudores = saldosPorCliente.Count;

        // Rendimiento por barbero
        var rendimientoBarberos = empleados.Select(e =>
        {
            var citasEmpleado = citasCerradas.Where(c => c.EmpleadoId == e.Id).ToList();
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
        .Where(r => r.ServiciosCount > 0 || periodo != "dia") // Mostrar todos en semana/mes
        .OrderByDescending(r => r.TotalGenerado)
        .ToList();

        // Top servicios
        var topServicios = citasCerradas
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

        return Ok(new DashboardCompleteDto
        {
            MetricasPeriodo = metricasPeriodo,
            Comparacion = comparacion,
            GraficoServicios = graficoServicios,
            GraficoIngresos = graficoIngresos,
            Nominas = resumenNominas,
            CuentasPorCobrar = cuentasPorCobrar,
            ClientesDeudores = clientesDeudores,
            RendimientoBarberos = rendimientoBarberos,
            TopServicios = topServicios,
            PeriodoNombre = periodoNombre,
            FechaInicio = inicioLocal,
            FechaFin = finLocal.AddDays(-1)
        });
    }

    // =========================================================
    // GET: api/dashboard/summary (mantener compatibilidad)
    // =========================================================
    /// <summary>Endpoint viejo que se mantiene por compatibilidad con apps antiguas: equivale al dashboard del dia.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DashboardCompleteDto>> GetSummary()
    {
        return await GetDashboard("dia");
    }

    // =========================================================
    // Helper: Generar datos para gráficos
    // =========================================================
    /// <summary>
    /// Agrupa las citas cerradas en "cubetas" para los graficos: por hora si el
    /// periodo es dia, por dia de la semana si es semana, por semana si es mes.
    /// Devuelve una tupla (servicios, ingresos) con una serie por grafico.
    /// </summary>
    private (List<DatoGraficoDto>, List<DatoGraficoDto>) GenerarDatosGrafico(
        string periodo,
        DateTime fechaInicio,   // límites en hora local del usuario
        DateTime fechaFin,
        DateTime hoyLocal,
        int offset,             // minutos; local = UTC - offset
        List<BarberShop.Models.Cita> citas)
    {
        var graficoServicios = new List<DatoGraficoDto>();
        var graficoIngresos = new List<DatoGraficoDto>();
        var citasCerradas = citas.Where(c => c.Estado == EstadoCita.Cerrada).ToList();

        // Fecha de la cita en hora local del usuario (se guarda en UTC)
        DateTime Local(BarberShop.Models.Cita c) => c.Fecha.AddMinutes(-offset);

        switch (periodo.ToLower())
        {
            case "dia":
                // Por hora (8am - 8pm)
                for (int hora = 8; hora <= 20; hora++)
                {
                    var citasHora = citasCerradas.Where(c => Local(c).Hour == hora).ToList();
                    var servicios = citasHora.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));
                    var ingresos = citasHora.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));

                    graficoServicios.Add(new DatoGraficoDto
                    {
                        Etiqueta = $"{hora}:00",
                        Fecha = fechaInicio.AddHours(hora),
                        Cantidad = servicios,
                        Valor = servicios
                    });

                    graficoIngresos.Add(new DatoGraficoDto
                    {
                        Etiqueta = $"{hora}:00",
                        Fecha = fechaInicio.AddHours(hora),
                        Valor = ingresos,
                        Cantidad = citasHora.Count
                    });
                }
                break;

            case "semana":
                // Por día de la semana
                var diasSemana = new[] { "Dom", "Lun", "Mar", "Mié", "Jue", "Vie", "Sáb" };
                for (int i = 0; i < 7; i++)
                {
                    var fecha = fechaInicio.AddDays(i);
                    if (fecha > hoyLocal) break;

                    var citasDia = citasCerradas.Where(c => Local(c).Date == fecha).ToList();
                    var servicios = citasDia.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));
                    var ingresos = citasDia.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));

                    graficoServicios.Add(new DatoGraficoDto
                    {
                        Etiqueta = diasSemana[(int)fecha.DayOfWeek],
                        Fecha = fecha,
                        Cantidad = servicios,
                        Valor = servicios
                    });

                    graficoIngresos.Add(new DatoGraficoDto
                    {
                        Etiqueta = diasSemana[(int)fecha.DayOfWeek],
                        Fecha = fecha,
                        Valor = ingresos,
                        Cantidad = citasDia.Count
                    });
                }
                break;

            case "mes":
                // Por semana del mes
                var semanaActual = 1;
                var fechaSemana = fechaInicio;
                while (fechaSemana < fechaFin)
                {
                    var finSemana = fechaSemana.AddDays(7);
                    if (finSemana > fechaFin) finSemana = fechaFin;

                    var citasSemana = citasCerradas
                        .Where(c => Local(c) >= fechaSemana && Local(c) < finSemana)
                        .ToList();

                    var servicios = citasSemana.Sum(c => c.CitaServicios.Sum(cs => cs.Cantidad));
                    var ingresos = citasSemana.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));

                    graficoServicios.Add(new DatoGraficoDto
                    {
                        Etiqueta = $"Sem {semanaActual}",
                        Fecha = fechaSemana,
                        Cantidad = servicios,
                        Valor = servicios
                    });

                    graficoIngresos.Add(new DatoGraficoDto
                    {
                        Etiqueta = $"Sem {semanaActual}",
                        Fecha = fechaSemana,
                        Valor = ingresos,
                        Cantidad = citasSemana.Count
                    });

                    fechaSemana = finSemana;
                    semanaActual++;
                }
                break;
        }

        return (graficoServicios, graficoIngresos);
    }
}