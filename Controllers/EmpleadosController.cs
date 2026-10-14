using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Gestion de empleados (barberos): alta, edicion, baja logica, detalle de
/// rendimiento y historial paginado de citas. Solo el Admin (dueno).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class EmpleadosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserContext _userContext;
    private readonly PlanValidationService _planService;


    /// <summary>Recibe BD, contexto del usuario y validador de planes por DI.</summary>
    public EmpleadosController(AppDbContext context, IUserContext userContext, PlanValidationService planService)
    {
        _context = context;
        _userContext = userContext;
        _planService = planService;

    }

    // =========================================================
    // POST: api/empleados
    // =========================================================
    /// <summary>Crea un empleado si el plan todavia admite mas (PLAN_LIMIT si no).</summary>
    [HttpPost]
    public async Task<IActionResult> Create(CreateEmpleadoDto dto)
    {
        var empresaId = _userContext.EmpresaId;
        if (empresaId == null)
            return Unauthorized("Empresa no detectada en el token");

        // ── Validar límite de plan ──
        var (permitido, error) = await _planService.PuedeCrearEmpleadoAsync(empresaId.Value);
        if (!permitido)
            return BadRequest(new { message = error, code = "PLAN_LIMIT" });

        var empleado = new Empleado
        {
            Nombre = dto.Nombre,
            PorcentajeComision = dto.PorcentajeComision,
            Periodicidad = dto.Periodicidad,
            EmpresaId = empresaId.Value
        };

        _context.Empleados.Add(empleado);
        await _context.SaveChangesAsync();

        return Ok(empleado);
    }

    // =========================================================
    // GET: api/empleados
    // =========================================================
    /// <summary>Lista los empleados activos de la empresa.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var empleados = await _context.Empleados.Where(e => e.Activo == true).ToListAsync();
        return Ok(empleados);
    }

    // =========================================================
    // GET: api/empleados/{id}
    // =========================================================
    /// <summary>Un empleado por id (activo o no); 404 si no existe en la empresa.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Empleado>> GetEmpleado(int id)
    {
        var empleado = await _context.Empleados.FirstOrDefaultAsync(c => c.Id == id);

        if (empleado == null)
            return NotFound();

        return empleado;
    }

    // =========================================================
    // GET: api/empleados/{id}/detalle
    // Detalle completo con rendimiento
    // =========================================================
    /// <summary>
    /// Ficha completa del empleado: comision pendiente, rendimiento de hoy/semana/
    /// mes y resumen de los ultimos 7 dias. Carga TODAS sus citas con
    /// .Include(CitaServicios) (eager loading: trae la relacion en la misma
    /// consulta) y calcula todo en memoria. Sencillo, pero crece con el historial;
    /// seria candidato a agregaciones en SQL como en ConsultasClientela.
    /// </summary>
    [HttpGet("{id:int}/detalle")]
    public async Task<ActionResult<EmpleadoDetalleDto>> GetDetalle(int id)
    {
        var empleado = await _context.Empleados.FindAsync(id);

        if (empleado == null)
            return NotFound("Empleado no encontrado");

        var hoy = DateTime.Today;
        var inicioSemana = hoy.AddDays(-(int)hoy.DayOfWeek);
        var inicioMes = new DateTime(hoy.Year, hoy.Month, 1);

        // Obtener todas las citas del empleado
        var todasLasCitas = await _context.Citas
            .Include(c => c.CitaServicios)
            .Where(c => c.EmpleadoId == id)
            .ToListAsync();

        // Citas no pagadas al empleado (para comisión pendiente)
        var citasNoPagadas = todasLasCitas
            .Where(c => c.Estado == EstadoCita.Cerrada && !c.PagadoAlEmpleado)
            .ToList();

        var comisionPendiente = citasNoPagadas.Sum(c =>
            c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad) * (empleado.PorcentajeComision / 100));

        // Calcular rendimiento por período
        var rendimientoHoy = CalcularRendimiento(todasLasCitas, hoy, hoy.AddDays(1), empleado.PorcentajeComision);
        var rendimientoSemana = CalcularRendimiento(todasLasCitas, inicioSemana, hoy.AddDays(1), empleado.PorcentajeComision);
        var rendimientoMes = CalcularRendimiento(todasLasCitas, inicioMes, hoy.AddDays(1), empleado.PorcentajeComision);

        // Últimos 7 días
        var ultimosDias = new List<ResumenDiarioDto>();
        for (int i = 6; i >= 0; i--)
        {
            var fecha = hoy.AddDays(-i);
            var citasDelDia = todasLasCitas
                .Where(c => c.Fecha.Date == fecha && c.Estado == EstadoCita.Cerrada)
                .ToList();

            var totalDia = citasDelDia.Sum(c => c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));

            ultimosDias.Add(new ResumenDiarioDto
            {
                Fecha = fecha,
                Citas = citasDelDia.Count,
                Total = totalDia,
                Comision = totalDia * (empleado.PorcentajeComision / 100)
            });
        }

        // Mapeo del enum a texto
        var periodicidadNombres = new Dictionary<PeriodicidadPago, string>
        {
            { PeriodicidadPago.Semanal, "Semanal" },
            { PeriodicidadPago.Quincenal, "Quincenal" },
            { PeriodicidadPago.Mensual, "Mensual" }
        };

        return Ok(new EmpleadoDetalleDto
        {
            Id = empleado.Id,
            Nombre = empleado.Nombre,
            PorcentajeComision = empleado.PorcentajeComision,
            Periodicidad = empleado.Periodicidad,
            PeriodicidadNombre = periodicidadNombres.GetValueOrDefault(empleado.Periodicidad, "Desconocido"),
            Activo = empleado.Activo,
            FechaUltimoPago = empleado.FechaUltimoPago,
            RendimientoHoy = rendimientoHoy,
            RendimientoSemana = rendimientoSemana,
            RendimientoMes = rendimientoMes,
            ComisionPendiente = comisionPendiente,
            CitasPendientesPago = citasNoPagadas.Count,
            UltimosDias = ultimosDias
        });
    }

    // =========================================================
    // PUT: api/empleados/{id}
    // =========================================================
    /// <summary>
    /// Edita el empleado. No deja cambiar el % de comision si tiene citas
    /// cerradas sin pagar: si no, la nomina pendiente se calcularia con el
    /// porcentaje nuevo en vez del vigente cuando se hizo el trabajo.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEmpleadoDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var empleado = await _context.Empleados.FindAsync(id);

        if (empleado == null)
            return NotFound("Empleado no encontrado.");

        if (empleado.PorcentajeComision != dto.PorcentajeComision)
        {
            var tieneComisionesSinProcesar = await _context.Citas.AnyAsync(c =>
                c.EmpleadoId == id &&
                c.Estado == EstadoCita.Cerrada &&
                !c.PagadoAlEmpleado);

            if (tieneComisionesSinProcesar)
            {
                return BadRequest(new
                {
                    message = "Prepara la nómina pendiente antes de cambiar el porcentaje de comisión."
                });
            }
        }

        empleado.Nombre = dto.Nombre;
        empleado.PorcentajeComision = dto.PorcentajeComision;
        empleado.Periodicidad = dto.Periodicidad;
        empleado.Activo = dto.Activo;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EmpleadoExists(id)) return NotFound();
            else throw;
        }

        return NoContent();
    }

    // =========================================================
    // DELETE: api/empleados/{id}
    // =========================================================
    /// <summary>Baja logica del empleado (Activo = false); sus citas se conservan.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var empleado = await _context.Empleados.FindAsync(id);
        if (empleado == null)
            return NotFound();

        empleado.Activo = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }
    // GET: api/empleados/{id}/historial?page=1&pageSize=20
    /// <summary>
    /// Historial de citas cerradas del empleado con paginacion clasica
    /// (Skip/Take = OFFSET/LIMIT en SQL). [FromQuery] toma page y pageSize de la
    /// query string (?page=2&amp;pageSize=20). Devuelve tambien el total para que
    /// la app sepa cuantas paginas hay.
    /// </summary>
    [HttpGet("{id:int}/historial")]
    public async Task<IActionResult> GetHistorial(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var empleado = await _context.Empleados.FindAsync(id);
        if (empleado == null)
            return NotFound("Empleado no encontrado");

        // Sin acotar, ?page=0 genera Skip(-20) y ArgumentException -> 500, y
        // ?pageSize=1000000 materializa el historial entero. Mismo criterio que
        // CitasController.Get, que ya lo hacia bien.
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Citas
            .AsNoTracking()
            .Include(c => c.CitaServicios)
            .Where(c => c.EmpleadoId == id && c.Estado == EstadoCita.Cerrada)
            .OrderByDescending(c => c.Fecha);

        var total = await query.CountAsync();

        var citas = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CitaHistorialDto
            {
                Id = c.Id,
                Fecha = c.Fecha,
                Total = c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad),
                Comision = c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad) * (empleado.PorcentajeComision / 100),
                PagadoAlEmpleado = c.PagadoAlEmpleado,
                NumeroServicios = c.CitaServicios.Count
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items = citas });
    }
    // =========================================================
    // Helpers privados
    // =========================================================
    /// <summary>true si el empleado existe (usado al capturar un conflicto de concurrencia).</summary>
    private bool EmpleadoExists(int id)
    {
        return (_context.Empleados?.Any(e => e.Id == id)).GetValueOrDefault();
    }

    /// <summary>
    /// Metodo puro (static, sin tocar BD): dado un listado de citas y un rango de
    /// fechas, cuenta citas, cerradas, total facturado y comision generada.
    /// </summary>
    private static RendimientoDto CalcularRendimiento(
        List<Cita> citas,
        DateTime desde,
        DateTime hasta,
        decimal porcentajeComision)
    {
        var citasPeriodo = citas
            .Where(c => c.Fecha >= desde && c.Fecha < hasta)
            .ToList();

        var citasCerradas = citasPeriodo
            .Where(c => c.Estado == EstadoCita.Cerrada)
            .ToList();

        var totalGenerado = citasCerradas.Sum(c =>
            c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad));

        return new RendimientoDto
        {
            TotalCitas = citasPeriodo.Count,
            CitasCerradas = citasCerradas.Count,
            TotalGenerado = totalGenerado,
            ComisionGenerada = totalGenerado * (porcentajeComision / 100)
        };
    }
}
