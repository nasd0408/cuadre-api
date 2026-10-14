using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Services;

public class DisponibilidadService(AppDbContext db)
{
    public async Task<DisponibilidadDto?> Consultar(int empleadoId, DateOnly fecha, int duracionMinutos, CancellationToken ct = default)
    {
        var empleado = await db.Empleados.AsNoTracking().SingleOrDefaultAsync(e => e.Id == empleadoId && e.Activo && e.EmpresaId == db.CurrentEmpresaId, ct);
        if (empleado is null || db.CurrentEmpresaId is null) return null;
        if (!empleado.HorarioAgendaActivo || empleado.ZonaHorariaAgenda is null)
            return new(false, empleado.ZonaHorariaAgenda ?? "UTC", []);
        var zona = TimeZoneInfo.FindSystemTimeZoneById(empleado.ZonaHorariaAgenda);
        var tramos = await db.TramosHorario.AsNoTracking().Where(t => t.EmpleadoId == empleadoId && t.DiaSemana == (int)fecha.DayOfWeek).ToListAsync(ct);
        var dia = fecha.ToDateTime(TimeOnly.MinValue);
        // Un margen UTC de un día cubre todas las zonas sin asumir un offset fijo.
        var limite = DateTime.SpecifyKind(dia.AddDays(2), DateTimeKind.Utc);
        var limiteInferior = DateTime.SpecifyKind(dia.AddDays(-1), DateTimeKind.Utc);
        var ausencias = await db.AusenciasEmpleados.AsNoTracking().Where(a => a.EmpleadoId == empleadoId && a.EmpresaId == db.CurrentEmpresaId && a.InicioUtc < limite && a.FinUtc > limiteInferior).ToListAsync(ct);
        var candidatas = await db.Citas.AsNoTracking().Where(c => c.EmpleadoId == empleadoId && c.Estado == EstadoCita.Pendiente && c.Fecha < limite)
            .Select(c => new { c.Fecha, Minutos = c.CitaServicios.Sum(s => (s.DuracionMinutos ?? s.Servicio.DuracionMinutos) * s.Cantidad) }).ToListAsync(ct);
        var libres = new SortedSet<DateTime>();
        var ahora = DateTime.UtcNow;
        foreach (var tramo in tramos)
            for (var minuto = tramo.InicioMinuto; minuto < tramo.FinMinuto; minuto += 15)
            {
                var inicio = HorarioService.AUTC(dia.AddMinutes(minuto), zona);
                if (inicio is null || inicio <= ahora || !HorarioService.Contiene(inicio.Value, duracionMinutos, zona, [tramo])) continue;
                var fin = inicio.Value.AddMinutes(duracionMinutos);
                if (ausencias.Any(a => a.InicioUtc < fin && a.FinUtc > inicio.Value)) continue;
                if (!candidatas.Any(c => c.Fecha < fin && c.Fecha.AddMinutes(Math.Max(1, c.Minutos)) > inicio.Value)) libres.Add(inicio.Value);
            }
        return new(true, zona.Id, libres.ToList());
    }
}
