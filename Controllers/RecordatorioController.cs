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
/// Recordatorios de "a este cliente ya le toca volver": si un servicio tiene
/// IntervaloDias (p. ej. corte cada 21 dias) y paso ese tiempo desde la
/// ultima cita, se genera un recordatorio para contactarlo. Modulo de pago.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class RecordatoriosController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IUserContext _userContext;
    private readonly PlanValidationService _planService;

    /// <summary>Recibe BD, contexto del usuario y el validador de planes.</summary>
    public RecordatoriosController(AppDbContext context, IUserContext userContext, PlanValidationService planService)
    {
        _context = context;
        _userContext = userContext;
        _planService = planService;
    }

    /// <summary>
    /// Lista los recordatorios pendientes o sin contestar. Antes de listar,
    /// genera los que falten (SincronizarAsync). El .Select a un objeto anonimo
    /// hace que EF traiga SOLO esas columnas (con JOIN a Cliente y Servicio).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPendientes()
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return Unauthorized();

        var (permitido, error) = await _planService.TieneModuloAsync(empresaId.Value, "recordatorios");
        if (!permitido)
            return StatusCode(403, new { message = error, code = "MODULE_DISABLED" });

        await SincronizarAsync();

        var recordatorios = await _context.Recordatorios
            .Where(r =>
                r.Estado == EstadoRecordatorio.Pendiente ||
                r.Estado == EstadoRecordatorio.NoContestó)
            .Select(r => new
            {
                r.Id,
                r.ClienteId,
                r.Estado,
                r.FechaCreacion,
                r.FechaGestion,
                r.Nota,
                ClienteNombre = r.Cliente.Nombre,
                ClienteTelefono = r.Cliente.Telefono,
                ServicioNombre = r.Servicio.Nombre,
                IntervaloDias = r.Servicio.IntervaloDias,
            })
            .OrderBy(r => r.Estado)
            .ThenBy(r => r.FechaCreacion)
            .ToListAsync();

        return Ok(recordatorios);
    }

    /// <summary>
    /// Marca un recordatorio como contactado / no contesto / descartado, con nota.
    /// PATCH es el verbo HTTP para modificar parcialmente un recurso.
    /// </summary>
    [HttpPatch("{id}/estado")]
    public async Task<IActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoRecordatorioDto dto)
    {
        var empresaId = _userContext.EmpresaId;
        if (!empresaId.HasValue) return Unauthorized();

        var (permitido, error) = await _planService.TieneModuloAsync(empresaId.Value, "recordatorios");
        if (!permitido)
            return StatusCode(403, new { message = error, code = "MODULE_DISABLED" });

        var recordatorio = await _context.Recordatorios.FindAsync(id);
        if (recordatorio == null) return NotFound();

        recordatorio.Estado = dto.Estado;
        recordatorio.FechaGestion = DateTime.UtcNow;
        recordatorio.Nota = dto.Nota;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Genera los recordatorios que falten: para la ultima cita cerrada de cada
    /// cliente, busca el servicio con intervalo mas vencido y crea un
    /// recordatorio si el cliente no tiene ya uno activo. Ojo: es un INSERT
    /// dentro de un GET y la comprobacion de duplicados es en memoria, asi que
    /// dos peticiones simultaneas pueden duplicar (ver nota en AppDbContext).
    /// </summary>
    private async Task SincronizarAsync()
    {
        var hoy = DateTime.UtcNow;

        // 1. Obtener todos los servicios activos con intervalo para tenerlos en memoria
        var serviciosDict = await _context.Servicios
            .Where(s => s.IntervaloDias != null && s.Activo)
            .ToDictionaryAsync(s => s.Id);

        // 2. Obtener la ULTIMA cita cerrada de cada cliente que tenga servicios con intervalo
        // Usamos una consulta agrupada para eficiencia
        var ultimasCitasPorCliente = await _context.Citas
            .Where(c => c.Estado == EstadoCita.Cerrada)
            .OrderByDescending(c => c.FechaCreacion)
            .Select(c => new
            {
                c.ClienteId,
                c.FechaCreacion,
                ServiciosIds = c.CitaServicios.Select(cs => cs.ServicioId).ToList()
            })
            .GroupBy(c => c.ClienteId)
            .Select(g => g.First()) // Nos quedamos con la más reciente de cada cliente
            .ToListAsync();

        // 3. Obtener recordatorios existentes para no duplicar
        var recordatoriosExistentes = await _context.Recordatorios
            .Where(r => r.Estado != EstadoRecordatorio.Descartado)
            .Select(r => r.ClienteId)
            .ToListAsync();

        foreach (var cita in ultimasCitasPorCliente)
        {
            // Si ya tiene un recordatorio activo, lo ignoramos por completo
            if (recordatoriosExistentes.Contains(cita.ClienteId)) continue;

            // 4. Encontrar el servicio de esa cita que esté "MÁS vencido"
            // Calculamos: (Fecha Cita + Intervalo) -> El que de la fecha más antigua es el más urgente
            var servicioMasVencido = cita.ServiciosIds
                .Where(id => serviciosDict.ContainsKey(id))
                .Select(id => new {
                    IntervaloDias = serviciosDict[id].IntervaloDias!.Value,
                    ServicioId = id,
                    FechaVencimiento = cita.FechaCreacion.AddDays(serviciosDict[id].IntervaloDias!.Value)
                })
                .Where(x => x.FechaVencimiento <= hoy)
                .OrderBy(x => x.FechaVencimiento) // El más antiguo primero
                .FirstOrDefault();

            if (servicioMasVencido != null)
            {
                _context.Recordatorios.Add(new Recordatorio
                {
                    ClienteId = cita.ClienteId,
                    ServicioId = servicioMasVencido.ServicioId, 
                    Estado = EstadoRecordatorio.Pendiente,
                    FechaCreacion = cita.FechaCreacion.AddDays(servicioMasVencido.IntervaloDias) // Opcional: registrar cuándo se detectó
                });

                // Evitamos agregar al mismo cliente en esta misma ejecución
                recordatoriosExistentes.Add(cita.ClienteId);
            }
        }

        await _context.SaveChangesAsync();
    }
}