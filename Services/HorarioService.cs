using BarberShop.Data;
using BarberShop.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Services;

public class HorarioService(AppDbContext db)
{
    // Las horas locales inexistentes o ambiguas por cambio de hora se omiten;
    // nunca se elige silenciosamente uno de dos instantes posibles.
    public static DateTime? AUTC(DateTime local, TimeZoneInfo zona)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return zona.IsInvalidTime(local) || zona.IsAmbiguousTime(local)
            ? null : TimeZoneInfo.ConvertTimeToUtc(local, zona);
    }

    public static bool Contiene(DateTime inicioUtc, int minutos, TimeZoneInfo zona, IEnumerable<TramoHorario> tramos)
    {
        var inicio = DateTime.SpecifyKind(inicioUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(inicio, zona);
        return tramos.Where(t => t.DiaSemana == (int)local.DayOfWeek).Any(t =>
        {
            var desde = AUTC(local.Date.AddMinutes(t.InicioMinuto), zona);
            var hasta = AUTC(local.Date.AddMinutes(t.FinMinuto), zona);
            return desde.HasValue && hasta.HasValue && inicio >= desde && inicio.AddMinutes(minutos) <= hasta;
        });
    }

    public async Task<bool> Permite(int empleadoId, DateTime inicio, int minutos)
    {
        var empleado = await db.Empleados.AsNoTracking().SingleOrDefaultAsync(e => e.Id == empleadoId);
        if (empleado is null || !empleado.Activo) return false;
        var fin = inicio.AddMinutes(minutos);
        if (await db.AusenciasEmpleados.AnyAsync(a => a.EmpleadoId == empleadoId && a.InicioUtc < fin && a.FinUtc > inicio)) return false;
        if (!empleado.HorarioAgendaActivo || empleado.ZonaHorariaAgenda is null) return true;
        var tramos = await db.TramosHorario.AsNoTracking().Where(t => t.EmpleadoId == empleadoId).ToListAsync();
        return Contiene(inicio, minutos, TimeZoneInfo.FindSystemTimeZoneById(empleado.ZonaHorariaAgenda), tramos);
    }
}
