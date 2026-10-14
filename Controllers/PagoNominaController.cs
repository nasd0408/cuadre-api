using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Dtos.BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Nomina de los empleados (modulo de pago). Flujo:
///   1. Cada cita Cerrada genera comision (% del empleado) sin procesar.
///   2. generar-nomina "congela" esas comisiones en un PagoNomina y marca las
///      citas como PagadoAlEmpleado (ya no se pueden reabrir).
///   3. El dueno paga la nomina con uno o varios abonos (Pendiente -> Parcial
///      -> Pagado).
/// </summary>
[ApiController]
[Route("api/pagos")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class PagosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserContext _userContext;
    private readonly PlanValidationService _planService;
    private readonly IPushNotificationService _pushService;
    private readonly ILogger<PagosController> _logger;

    /// <summary>Recibe BD, contexto de usuario, validador de planes, push y logger por DI.</summary>
    public PagosController(
        AppDbContext context,
        IUserContext userContext,
        PlanValidationService planService,
        IPushNotificationService pushService,
        ILogger<PagosController> logger)
    {
        _context = context;
        _userContext = userContext;
        _planService = planService;
        _pushService = pushService;
        _logger = logger;
    }

    // =========================================================
    // GET: api/pagos/resumen
    // Resumen general de nóminas pendientes
    // =========================================================
    /// <summary>
    /// Resumen para todos los empleados: comision pendiente y nominas por pagar.
    /// Hace 2 consultas en total en vez de 2 por empleado (evita el problema
    /// "N+1": un bucle que lanza una consulta por cada elemento).
    /// </summary>
    [HttpGet("resumen")]
    public async Task<ActionResult<ResumenGeneralNominasDto>> GetResumen()
    {

        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;
        // Incluye empleados inactivos con saldos pendientes para que una baja
        // nunca oculte comisiones o nóminas aún por pagar.
        var empleados = await _context.Empleados.ToListAsync();

        // Dos consultas para TODOS los empleados, no dos por empleado.
        //
        // Antes este bucle hacia un SELECT de citas y otro de nominas en cada
        // vuelta: con 20 empleados eran 41 viajes a la base de datos para pintar
        // una sola pantalla. Ahora son tres fijos, sin importar cuantos haya.
        var citasPorEmpleado = (await _context.Citas
                .AsNoTracking()
                .Include(c => c.CitaServicios)
                .Where(c => c.Estado == EstadoCita.Cerrada && !c.PagadoAlEmpleado)
                .ToListAsync())
            .GroupBy(c => c.EmpleadoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var nominasPorEmpleado = (await _context.PagosNomina
                .AsNoTracking()
                .Include(p => p.Abonos)
                .Where(p => p.Estado != EstadoPago.Pagado)
                .OrderByDescending(p => p.FechaCorte)
                .ToListAsync())
            .GroupBy(p => p.EmpleadoId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var resumenEmpleados = new List<ResumenNominaEmpleadoDto>();
        decimal totalPendiente = 0;
        int empleadosConPendientes = 0;

        foreach (var empleado in empleados)
        {
            var citasNoPagadas = citasPorEmpleado.TryGetValue(empleado.Id, out var cs)
                ? cs
                : new List<Cita>();

            var comisionPendiente = citasNoPagadas.Sum(c =>
                c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad) * (empleado.PorcentajeComision / 100));

            var nominasPendientes = nominasPorEmpleado.TryGetValue(empleado.Id, out var ns)
                ? ns
                : new List<PagoNomina>();

            var nominasDto = nominasPendientes.Select(n => MapToNominaResponse(n, empleado.Nombre)).ToList();

            var totalEmpleadoPendiente = comisionPendiente + nominasPendientes.Sum(n => n.MontoTotal - n.MontoPagado);

            if (totalEmpleadoPendiente > 0 || citasNoPagadas.Any())
            {
                empleadosConPendientes++;
                totalPendiente += totalEmpleadoPendiente;

                resumenEmpleados.Add(new ResumenNominaEmpleadoDto
                {
                    EmpleadoId = empleado.Id,
                    EmpleadoNombre = empleado.Nombre,
                    PorcentajeComision = empleado.PorcentajeComision,
                    CitasPendientes = citasNoPagadas.Count,
                    ComisionPendiente = comisionPendiente,
                    UltimoPago = empleado.FechaUltimoPago,
                    NominasPendientes = nominasDto
                });
            }
        }

        return Ok(new ResumenGeneralNominasDto
        {
            TotalPendientePagar = totalPendiente,
            EmpleadosConPendientes = empleadosConPendientes,
            Empleados = resumenEmpleados.OrderByDescending(e => e.ComisionPendiente).ToList()
        });
    }

    // =========================================================
    // GET: api/pagos/empleado/{empleadoId}
    // Detalle de pagos de un empleado
    // =========================================================
    /// <summary>Comision pendiente y todas las nominas (con abonos) de un empleado.</summary>
    [HttpGet("empleado/{empleadoId}")]
    public async Task<IActionResult> GetPagosEmpleado(int empleadoId)
    {
        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;

        var empleado = await _context.Empleados.FindAsync(empleadoId);
        if (empleado == null) return NotFound("Empleado no encontrado");

        // Comisión pendiente (citas cerradas no procesadas)
        var citasNoPagadas = await _context.Citas
            .Include(c => c.CitaServicios)
            .Where(c => c.EmpleadoId == empleadoId &&
                        c.Estado == EstadoCita.Cerrada &&
                        !c.PagadoAlEmpleado)
            .ToListAsync();

        var comisionPendiente = citasNoPagadas.Sum(c =>
            c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad) * (empleado.PorcentajeComision / 100));

        // Todas las nóminas del empleado
        var nominas = await _context.PagosNomina
            .Include(p => p.Abonos)
            .Where(p => p.EmpleadoId == empleadoId)
            .OrderByDescending(p => p.FechaCorte)
            .ToListAsync();

        return Ok(new
        {
            Empleado = new
            {
                empleado.Id,
                empleado.Nombre,
                empleado.PorcentajeComision,
                empleado.FechaUltimoPago
            },
            ComisionSinProcesar = new
            {
                CitasCount = citasNoPagadas.Count,
                Monto = comisionPendiente,
                CitasIds = citasNoPagadas.Select(c => c.Id).ToList()
            },
            Nominas = nominas.Select(n => MapToNominaResponse(n, empleado.Nombre)).ToList()
        });
    }

    // =========================================================
    // GET: api/pagos/pendientes/{empleadoId}
    // Comisiones pendientes de un empleado (sin nómina generada)
    // =========================================================
    /// <summary>Detalle de las citas cerradas cuya comision aun no entro en ninguna nomina.</summary>
    [HttpGet("pendientes/{empleadoId}")]
    public async Task<IActionResult> GetComisionesPendientes(int empleadoId)
    {
        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;

        var empleado = await _context.Empleados.FindAsync(empleadoId);
        if (empleado == null) return NotFound("Empleado no encontrado");

        var citasNoPagadas = await _context.Citas
            .Include(c => c.CitaServicios)
                .ThenInclude(cs => cs.Servicio)
            .Include(c => c.Cliente)
            .Where(c => c.EmpleadoId == empleadoId &&
                        c.Estado == EstadoCita.Cerrada &&
                        !c.PagadoAlEmpleado)
            .OrderBy(c => c.Fecha)
            .ToListAsync();

        var detalles = citasNoPagadas.Select(c =>
        {
            var total = c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad);
            return new
            {
                CitaId = c.Id,
                Fecha = c.Fecha,
                ClienteNombre = c.Cliente?.Nombre ?? "Sin cliente",
                Servicios = c.CitaServicios.Select(cs => cs.Servicio?.Nombre ?? "Servicio").ToList(),
                Total = total,
                Comision = total * (empleado.PorcentajeComision / 100)
            };
        }).ToList();

        var totalComision = detalles.Sum(d => d.Comision);

        return Ok(new
        {
            Empleado = empleado.Nombre,
            PorcentajeComision = empleado.PorcentajeComision,
            CantidadCitas = citasNoPagadas.Count,
            TotalComisionAcumulada = totalComision,
            Detalle = detalles
        });
    }

    // =========================================================
    // POST: api/pagos/generar-nomina/{empleadoId}
    // Genera una nómina (congela las comisiones)
    // =========================================================
    /// <summary>
    /// Crea la nomina con el total de comisiones pendientes y marca esas citas
    /// como procesadas, en una transaccion. Se envuelve en una "execution
    /// strategy" de EF (hoy no reintenta; ver nota en Program.cs). Tras el
    /// commit intenta enviar un push; si el push falla, la nomina ya quedo
    /// guardada y solo se registra un warning.
    /// </summary>
    [HttpPost("generar-nomina/{empleadoId}")]
    public async Task<IActionResult> GenerarNomina(int empleadoId)
    {
        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var empleado = await _context.Empleados.FindAsync(empleadoId);
                if (empleado == null) return NotFound("Empleado no encontrado");

                var citasNoPagadas = await _context.Citas
                    .Include(c => c.CitaServicios)
                    .Where(c => c.EmpleadoId == empleadoId &&
                                c.Estado == EstadoCita.Cerrada &&
                                !c.PagadoAlEmpleado)
                    .ToListAsync();

                if (!citasNoPagadas.Any())
                    return BadRequest("No hay comisiones pendientes para este empleado.");

                var montoTotal = citasNoPagadas.Sum(c =>
                    c.CitaServicios.Sum(cs => cs.Precio * cs.Cantidad) * (empleado.PorcentajeComision / 100));

                var nomina = new PagoNomina
                {
                    EmpleadoId = empleadoId,
                    FechaCorte = DateTime.UtcNow,
                    MontoTotal = montoTotal,
                    MontoPagado = 0,
                    Estado = EstadoPago.Pendiente
                };

                _context.PagosNomina.Add(nomina);

                // Marcar citas como procesadas
                foreach (var cita in citasNoPagadas)
                {
                    cita.PagadoAlEmpleado = true;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                try
                {
                    var empresaId = _userContext.EmpresaId;
                    if (empresaId.HasValue)
                    {
                        var prefs = await _pushService.GetPreferenciasAsync(empresaId.Value);
                        if (prefs.AvisoNomina)
                        {
                            await _pushService.SendToEmpresaAsync(
                                empresaId.Value,
                                "Nómina lista para pagar",
                                $"Se generó la nómina de {empleado.Nombre} por ${montoTotal:N2}.",
                                "Nominas");
                        }
                    }
                }
                catch (Exception pushException)
                {
                    _logger.LogWarning(
                        pushException,
                        "La nómina {NominaId} se generó, pero no se pudo enviar el push",
                        nomina.Id);
                }

                return Ok(new
                {
                    Message = "Nómina generada exitosamente",
                    NominaId = nomina.Id,
                    CitasProcesadas = citasNoPagadas.Count,
                    MontoTotal = montoTotal
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                // Genera dinero a pagar: si falla hay que poder reconstruir por que.
                _logger.LogError(ex,
                    "Error al generar la nómina de la empresa {EmpresaId}",
                    _userContext.EmpresaId);
                return StatusCode(500, "No pudimos generar la nómina. Inténtalo de nuevo.");
            }
        });
    }
    // =========================================================
    // POST: api/pagos/abonar
    // Registra un abono a una nómina
    // =========================================================
    /// <summary>
    /// Registra un pago parcial de una nomina, sin permitir pagar de mas.
    /// Actualiza el estado (Parcial o Pagado) y, si queda saldada, la fecha de
    /// ultimo pago del empleado. PagoNomina tiene token de concurrencia (xmin),
    /// asi que dos abonos simultaneos no pueden pisarse.
    /// </summary>
    [HttpPost("abonar")]
    public async Task<IActionResult> AbonarNomina([FromBody] CrearAbonoNominaDto dto)
    {
        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;

        if (dto.Monto <= 0)
            return BadRequest("El monto debe ser mayor a 0");

        var nomina = await _context.PagosNomina
            .Include(p => p.Abonos)
            .Include(p => p.Empleado)
            .FirstOrDefaultAsync(p => p.Id == dto.PagoNominaId);

        if (nomina == null) return NotFound("Nómina no encontrada");
        if (nomina.Estado == EstadoPago.Pagado) return BadRequest("Esta nómina ya está totalmente pagada.");

        var pendiente = nomina.MontoTotal - nomina.MontoPagado;
        if (dto.Monto > pendiente)
            return BadRequest($"El monto excede el pendiente. Máximo a abonar: ${pendiente:N2}");

        var abono = new AbonoPago
        {
            PagoNominaId = nomina.Id,
            Monto = dto.Monto,
            FechaAbono = DateTime.UtcNow
        };

        nomina.MontoPagado += dto.Monto;

        // Actualizar estado
        if (nomina.MontoPagado >= nomina.MontoTotal)
        {
            nomina.Estado = EstadoPago.Pagado;
            // Actualizar fecha último pago del empleado
            if (nomina.Empleado != null)
            {
                nomina.Empleado.FechaUltimoPago = DateTime.UtcNow;
            }
        }
        else
        {
            nomina.Estado = EstadoPago.Parcial;
        }

        _context.AbonosPago.Add(abono);
        await _context.SaveChangesAsync();

        return Ok(MapToNominaResponse(nomina, nomina.Empleado?.Nombre ?? ""));
    }

    // =========================================================
    // POST: api/pagos/pagar-completo/{nominaId}
    // Paga una nómina completa de un solo golpe
    // =========================================================
    /// <summary>Paga de una vez lo que falte de la nomina con un unico abono.</summary>
    [HttpPost("pagar-completo/{nominaId}")]
    public async Task<IActionResult> PagarCompleto(int nominaId)
    {
        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;

        var nomina = await _context.PagosNomina
            .Include(p => p.Abonos)
            .Include(p => p.Empleado)
            .FirstOrDefaultAsync(p => p.Id == nominaId);

        if (nomina == null) return NotFound("Nómina no encontrada");
        if (nomina.Estado == EstadoPago.Pagado) return BadRequest("Esta nómina ya está pagada.");

        var pendiente = nomina.MontoTotal - nomina.MontoPagado;

        var abono = new AbonoPago
        {
            PagoNominaId = nomina.Id,
            Monto = pendiente,
            FechaAbono = DateTime.UtcNow
        };

        nomina.MontoPagado = nomina.MontoTotal;
        nomina.Estado = EstadoPago.Pagado;

        if (nomina.Empleado != null)
        {
            nomina.Empleado.FechaUltimoPago = DateTime.UtcNow;
        }

        _context.AbonosPago.Add(abono);
        await _context.SaveChangesAsync();

        return Ok(MapToNominaResponse(nomina, nomina.Empleado?.Nombre ?? ""));
    }

    // =========================================================
    // GET: api/pagos/nomina/{nominaId}
    // Detalle de una nómina específica
    // =========================================================
    /// <summary>Detalle de una nomina con sus abonos.</summary>
    [HttpGet("nomina/{nominaId}")]
    public async Task<IActionResult> GetNomina(int nominaId)
    {
        var bloqueado = await ValidarModuloPagos();
        if (bloqueado != null) return bloqueado;

        var nomina = await _context.PagosNomina
            .Include(p => p.Abonos)
            .Include(p => p.Empleado)
            .FirstOrDefaultAsync(p => p.Id == nominaId);

        if (nomina == null) return NotFound("Nómina no encontrada");

        return Ok(MapToNominaResponse(nomina, nomina.Empleado?.Nombre ?? ""));
    }

    // =========================================================
    // Helper: Validar acceso al modulo
    // =========================================================
    /// <summary>
    /// Devuelve null si la empresa tiene el modulo de pagos, o la respuesta de
    /// error (401/403) lista para devolver. Patron "guard clause" reutilizable:
    /// if (bloqueado != null) return bloqueado;
    /// </summary>
    private async Task<ActionResult?> ValidarModuloPagos()
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return new UnauthorizedResult();

        var (permitido, error) = await _planService.TieneModuloAsync(empresaId.Value, "pagos");
        if (!permitido)
            return new ObjectResult(new { message = error, code = "MODULE_DISABLED" }) { StatusCode = 403 };

        return null;
    }


    // =========================================================
    // Helper: Mapear a DTO
    // =========================================================
    /// <summary>Convierte PagoNomina (con sus abonos) al DTO de respuesta.</summary>
    private static PagoNominaResponseDto MapToNominaResponse(PagoNomina nomina, string empleadoNombre)
    {
        var estadoNombres = new Dictionary<EstadoPago, string>
        {
            { EstadoPago.Pendiente, "Pendiente" },
            { EstadoPago.Parcial, "Parcial" },
            { EstadoPago.Pagado, "Pagado" }
        };

        return new PagoNominaResponseDto
        {
            Id = nomina.Id,
            EmpleadoId = nomina.EmpleadoId,
            EmpleadoNombre = empleadoNombre,
            FechaCorte = nomina.FechaCorte,
            MontoTotal = nomina.MontoTotal,
            MontoPagado = nomina.MontoPagado,
            Estado = (int)nomina.Estado,
            EstadoNombre = estadoNombres.GetValueOrDefault(nomina.Estado, "Desconocido"),
            Abonos = nomina.Abonos?.Select(a => new AbonoResponseDto
            {
                Id = a.Id,
                Monto = a.Monto,
                FechaAbono = a.FechaAbono
            }).OrderByDescending(a => a.FechaAbono).ToList() ?? new List<AbonoResponseDto>()
        };
    }
}
