using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers;

[ApiController, Route("api/empleados/{empleadoId:int}/horario"), Authorize]
public class HorariosController(AppDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Get(int empleadoId)
    {
        var empleado = await db.Empleados.AsNoTracking().SingleOrDefaultAsync(e => e.Id == empleadoId);
        if (empleado is null || db.CurrentEmpresaId is null) return NotFound();
        return Ok(new HorarioDto
        {
            Activo = empleado.HorarioAgendaActivo,
            ZonaHoraria = empleado.ZonaHorariaAgenda ?? "UTC",
            Tramos = await db.TramosHorario.Where(t => t.EmpleadoId == empleadoId)
                .OrderBy(t => t.DiaSemana).ThenBy(t => t.InicioMinuto)
                .Select(t => new TramoHorarioDto { DiaSemana = t.DiaSemana, InicioMinuto = t.InicioMinuto, FinMinuto = t.FinMinuto }).ToListAsync()
        });
    }

    [HttpPut, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Put(int empleadoId, HorarioDto dto)
    {
        await using var transaccion = await TransaccionAgenda.Abrir(db);
        var empleado = await db.Empleados.SingleOrDefaultAsync(e => e.Id == empleadoId && e.EmpresaId == db.CurrentEmpresaId);
        if (empleado is null || db.CurrentEmpresaId is null) return NotFound();
        var anteriores = await db.TramosHorario.Where(t => t.EmpleadoId == empleadoId).ToListAsync();
        db.TramosHorario.RemoveRange(anteriores);
        db.TramosHorario.AddRange(dto.Tramos.Select(t => new TramoHorario
        {
            EmpresaId = db.CurrentEmpresaId, EmpleadoId = empleadoId,
            DiaSemana = t.DiaSemana, InicioMinuto = t.InicioMinuto, FinMinuto = t.FinMinuto
        }));
        empleado.ZonaHorariaAgenda = dto.ZonaHoraria;
        empleado.HorarioAgendaActivo = dto.Activo;
        await db.SaveChangesAsync();
        await transaccion.CommitAsync();
        return await Get(empleadoId);
    }

    [HttpGet("disponibilidad"), Authorize(Roles = "Admin,Barbero,SuperAdmin")]
    public async Task<IActionResult> Disponibilidad(int empleadoId, [FromQuery] DateOnly fecha, [FromQuery] int duracionMinutos = 30)
    {
        if (duracionMinutos < 1 || duracionMinutos > 600 || fecha.Year < 2000 || fecha.Year > 2100)
            return BadRequest("Indica una fecha válida y una duración entre 1 y 600 minutos.");
        var disponibilidad = await new DisponibilidadService(db).Consultar(empleadoId, fecha, duracionMinutos);
        return disponibilidad is null ? NotFound() : Ok(disponibilidad);
    }
}
