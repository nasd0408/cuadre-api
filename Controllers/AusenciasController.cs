using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers;

[ApiController, Route("api/empleados/{empleadoId:int}/ausencias"), Authorize(Roles = "Admin")]
public class AusenciasController(AppDbContext db) : ControllerBase
{
    private Task<bool> EsPropio(int id) => db.Empleados.AnyAsync(e => e.Id == id && e.EmpresaId == db.CurrentEmpresaId && db.CurrentEmpresaId != null);

    [HttpGet]
    public async Task<IActionResult> Get(int empleadoId)
    {
        if (!await EsPropio(empleadoId)) return NotFound();
        return Ok(await db.AusenciasEmpleados.AsNoTracking().Where(a => a.EmpleadoId == empleadoId && a.EmpresaId == db.CurrentEmpresaId && a.FinUtc > DateTime.UtcNow)
            .OrderBy(a => a.InicioUtc).Select(a => new AusenciaDto(a.Id, a.Desde, a.Hasta, a.ZonaHoraria, a.Motivo)).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(int empleadoId, CrearAusenciaDto dto)
    {
        await using var transaccion = await TransaccionAgenda.Abrir(db);
        var empleado = await db.Empleados.SingleOrDefaultAsync(e => e.Id == empleadoId && e.EmpresaId == db.CurrentEmpresaId && db.CurrentEmpresaId != null);
        if (empleado is null) return NotFound();
        if (empleado.ZonaHorariaAgenda is null) return BadRequest("Guarda primero la zona horaria del profesional.");
        var zona = TimeZoneInfo.FindSystemTimeZoneById(empleado.ZonaHorariaAgenda);
        var inicio = HorarioService.AUTC(dto.Desde.ToDateTime(TimeOnly.MinValue), zona);
        var fin = HorarioService.AUTC(dto.Hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), zona);
        if (inicio is null || fin is null || fin <= inicio || fin <= DateTime.UtcNow)
            return BadRequest("El rango ha terminado o contiene un límite horario ambiguo o inexistente.");
        if (await db.AusenciasEmpleados.AnyAsync(a => a.EmpleadoId == empleadoId && a.InicioUtc < fin && a.FinUtc > inicio))
            return Conflict("Ya hay una ausencia que coincide con esas fechas.");
        var citas = await db.Citas.Where(c => c.EmpleadoId == empleadoId && c.Estado == EstadoCita.Pendiente && c.Fecha < fin)
            .Select(c => new { c.Fecha, Minutos = c.CitaServicios.Sum(s => (s.DuracionMinutos ?? s.Servicio.DuracionMinutos) * s.Cantidad) }).ToListAsync();
        if (citas.Any(c => c.Fecha.AddMinutes(Math.Max(1, c.Minutos)) > inicio))
            return Conflict("Hay citas pendientes en ese período. Reprográmalas antes de registrar la ausencia.");
        var ausencia = new AusenciaEmpleado { EmpresaId = db.CurrentEmpresaId, EmpleadoId = empleadoId,
            Desde = dto.Desde, Hasta = dto.Hasta, ZonaHoraria = zona.Id, InicioUtc = inicio.Value, FinUtc = fin.Value, Motivo = dto.Motivo?.Trim() };
        db.AusenciasEmpleados.Add(ausencia);
        await db.SaveChangesAsync();
        await transaccion.CommitAsync();
        return StatusCode(201, new AusenciaDto(ausencia.Id, ausencia.Desde, ausencia.Hasta, ausencia.ZonaHoraria, ausencia.Motivo));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int empleadoId, int id)
    {
        await using var transaccion = await TransaccionAgenda.Abrir(db);
        if (!await EsPropio(empleadoId)) return NotFound();
        var ausencia = await db.AusenciasEmpleados.SingleOrDefaultAsync(a => a.Id == id && a.EmpleadoId == empleadoId && a.EmpresaId == db.CurrentEmpresaId);
        if (ausencia is null) return NotFound();
        db.AusenciasEmpleados.Remove(ausencia);
        await db.SaveChangesAsync();
        await transaccion.CommitAsync();
        return NoContent();
    }
}
