using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Hubs;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Controllers;

[ApiController, Route("api/empresa/reservas-publicas"), Authorize(Roles = "Admin")]
public class ConfiguracionReservasController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        var perfil = await db.PerfilesPublicos.AsNoTracking().SingleOrDefaultAsync(p => p.EmpresaId == empresaId, ct);
        return Ok(new ConfiguracionReservasDto(perfil?.ReservasActivas ?? false, perfil?.SucursalReservasId,
            perfil?.TokenReservas, await Profesionales(db, ct)));
    }

    [HttpPut]
    public async Task<IActionResult> Put(ConfigurarReservasDto dto, CancellationToken ct = default)
    {
        if (db.CurrentEmpresaId is not int empresaId) return Forbid();
        await using var transaction = await TransaccionAgenda.Abrir(db, ct);
        var perfil = await db.PerfilesPublicos.SingleOrDefaultAsync(p => p.EmpresaId == empresaId, ct);
        if (perfil is null) return BadRequest("Guarda primero el perfil público del negocio.");
        if (dto.Activas)
        {
            if (!perfil.Publicado) return BadRequest("Publica primero el perfil del negocio.");
            if (!await db.Sucursales.AnyAsync(s => s.Id == dto.SucursalId && s.EmpresaId == empresaId && s.Publicada, ct))
                return BadRequest("Selecciona un local publicado para recibir las citas.");
            if ((await Profesionales(db, ct)).Count == 0 || !await db.Servicios.AnyAsync(s => s.Activo && s.EmpresaId == empresaId, ct))
                return BadRequest("Necesitas servicios activos y al menos un profesional con horario configurado.");
            perfil.TokenReservas ??= Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            perfil.SucursalReservasId = dto.SucursalId;
        }
        perfil.ReservasActivas = dto.Activas;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await Get(ct);
    }

    internal static Task<List<ProfesionalPublicoDto>> Profesionales(AppDbContext db, CancellationToken ct) =>
        db.Empleados.AsNoTracking().Where(e => e.EmpresaId == db.CurrentEmpresaId && e.Activo && e.HorarioAgendaActivo && e.ZonaHorariaAgenda != null &&
            db.TramosHorario.Any(t => t.EmpleadoId == e.Id && t.EmpresaId == db.CurrentEmpresaId))
            .OrderBy(e => e.Nombre).Select(e => new ProfesionalPublicoDto(e.Id, e.Nombre, e.ZonaHorariaAgenda!)).ToListAsync(ct);
}

[ApiController, Route("api/publico/reservas"), AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ReservasPublicasController(AppDbContext db, DbContextOptions<AppDbContext> options,
    IHubContext<AgendaHub> hub, ILogger<ReservasPublicasController> logger) : ControllerBase
{
    private static bool TokenValido(string token) => Regex.IsMatch(token, "^[a-f0-9]{64}$");
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class EmpresaPublicaContext(int empresaId) : IUserContext
    {
        public int? EmpresaId => empresaId;
        public string UsuarioId => "";
        public bool IsSuperAdmin => false;
    }

    private AppDbContext Contexto(int empresaId) => new(options, new EmpresaPublicaContext(empresaId));

    private async Task<PerfilPublicoEmpresa?> Resolver(string token, CancellationToken ct)
    {
        if (!TokenValido(token)) return null;
        // Solo la resolución del enlace atraviesa tenants. El JWT del visitante no interviene.
        return await db.PerfilesPublicos.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(p =>
            p.TokenReservas == token && p.Publicado && p.ReservasActivas &&
            db.Empresas.Any(e => e.Id == p.EmpresaId && e.Estado != EstadoSuscripcion.Suspendido &&
                (!e.FechaBloqueo.HasValue || e.FechaBloqueo >= DateTime.UtcNow)), ct);
    }

    private static async Task<Sucursal?> Local(AppDbContext tenant, PerfilPublicoEmpresa perfil, CancellationToken ct) =>
        await tenant.Sucursales.AsNoTracking().SingleOrDefaultAsync(s => s.Id == perfil.SucursalReservasId && s.Publicada, ct);

    [HttpGet("{token}"), EnableRateLimiting("public-read")]
    public async Task<IActionResult> Get(string token, CancellationToken ct = default)
    {
        var perfil = await Resolver(token, ct);
        if (perfil?.EmpresaId is not int empresaId) return NotFound();
        await using var tenant = Contexto(empresaId);
        var local = await Local(tenant, perfil, ct);
        if (local is null) return NotFound();
        var servicios = await tenant.Servicios.AsNoTracking().Where(s => s.Activo).OrderBy(s => s.Nombre)
            .Select(s => new ServicioPublicoDto(s.Id, s.Nombre, s.DuracionMinutos, s.Precio)).ToListAsync(ct);
        return Ok(new CatalogoReservasDto(perfil.Nombre, perfil.Descripcion, perfil.TelefonoPublico,
            SucursalesController.Map(local), await ConfiguracionReservasController.Profesionales(tenant, ct), servicios));
    }

    [HttpGet("{token}/disponibilidad"), EnableRateLimiting("public-read")]
    public async Task<IActionResult> Disponibilidad(string token, [FromQuery] int empleadoId,
        [FromQuery] int servicioId, [FromQuery] DateOnly fecha, CancellationToken ct = default)
    {
        if (fecha < DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)) || fecha > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)))
            return BadRequest("Selecciona una fecha dentro de los próximos 90 días.");
        var perfil = await Resolver(token, ct);
        if (perfil?.EmpresaId is not int empresaId) return NotFound();
        await using var tenant = Contexto(empresaId);
        if (await Local(tenant, perfil, ct) is null) return NotFound();
        var servicio = await tenant.Servicios.AsNoTracking().SingleOrDefaultAsync(s => s.Id == servicioId && s.Activo, ct);
        if (servicio is null) return NotFound();
        var disponibilidad = await new DisponibilidadService(tenant).Consultar(empleadoId, fecha, servicio.DuracionMinutos, ct);
        return disponibilidad is null ? NotFound() : Ok(disponibilidad);
    }

    [HttpPost("{token}"), EnableRateLimiting("public-book")]
    public async Task<IActionResult> Crear(string token, CrearReservaPublicaDto dto, CancellationToken ct = default)
    {
        if (!TokenValido(dto.SolicitudToken)) return BadRequest("La solicitud no es válida.");
        var nombre = dto.Nombre.Trim();
        var telefono = Regex.Replace(dto.Telefono, "[ ()-]", "");
        var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        if (nombre.Length < 2 || nombre.Length > 150 || !Regex.IsMatch(telefono, @"^\+?[0-9]{7,15}$"))
            return BadRequest("Indica tu nombre y un teléfono válido con prefijo de país.");
        var perfil = await Resolver(token, ct);
        if (perfil?.EmpresaId is not int empresaId) return NotFound();
        await using var tenant = Contexto(empresaId);
        await using var transaction = await TransaccionAgenda.Abrir(tenant, ct);
        // Releer después del bloqueo: cerrar la agenda y reservar también se serializan.
        perfil = await Resolver(token, ct);
        if (perfil is null) return NotFound();
        var local = await Local(tenant, perfil, ct);
        if (local is null) return NotFound();
        var tokenHash = Hash(dto.SolicitudToken);
        var solicitudHash = Hash(JsonSerializer.Serialize(new { empresaId, dto.EmpleadoId, dto.ServicioId,
            inicio = dto.InicioUtc.UtcDateTime, nombre, telefono, email }));
        var anterior = await db.ReservasPublicas.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == tokenHash, ct);
        if (anterior is not null)
        {
            if (anterior.EmpresaId != empresaId || anterior.SolicitudHash != solicitudHash)
                return Conflict("Esta solicitud ya fue utilizada. Inicia una nueva reserva.");
            return Ok(await Confirmacion(tenant, anterior.CitaId, ct));
        }
        var inicio = dto.InicioUtc.UtcDateTime;
        if (inicio <= DateTime.UtcNow || inicio > DateTime.UtcNow.AddDays(90))
            return BadRequest("Selecciona un horario futuro dentro de los próximos 90 días.");
        var empleado = await tenant.Empleados.AsNoTracking().SingleOrDefaultAsync(e => e.Id == dto.EmpleadoId && e.Activo && e.HorarioAgendaActivo && e.ZonaHorariaAgenda != null, ct);
        var servicio = await tenant.Servicios.AsNoTracking().SingleOrDefaultAsync(s => s.Id == dto.ServicioId && s.Activo, ct);
        if (empleado is null || servicio is null) return NotFound();
        var fechaLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(inicio, TimeZoneInfo.FindSystemTimeZoneById(empleado.ZonaHorariaAgenda!)));
        var disponibilidad = await new DisponibilidadService(tenant).Consultar(empleado.Id, fechaLocal, servicio.DuracionMinutos, ct);
        if (disponibilidad?.IniciosUtc.Contains(inicio) != true)
            return Conflict("Ese horario ya no está disponible. Elige otro horario.");
        // Un teléfono sin verificar nunca permite acceder ni modificar una ficha existente.
        var cliente = new Cliente { Nombre = nombre, Telefono = telefono };
        var cita = new Cita { Cliente = cliente, EmpleadoId = empleado.Id, Fecha = inicio, Estado = EstadoCita.Pendiente };
        cita.CitaServicios.Add(new CitaServicio { ServicioId = servicio.Id, Precio = servicio.Precio,
            DuracionMinutos = servicio.DuracionMinutos, Cantidad = 1 });
        tenant.ReservasPublicas.Add(new ReservaPublica { Cita = cita, TokenHash = tokenHash, SolicitudHash = solicitudHash,
            NombreCliente = nombre, Telefono = telefono, Email = email, NombreLocal = local.Nombre, DireccionLocal = local.DireccionPublica });
        await tenant.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await AgendaNotificationFilter.Notificar(hub, logger, empresaId);
        return StatusCode(StatusCodes.Status201Created, await Confirmacion(tenant, cita.Id, ct));
    }

    [HttpGet("cita/{token}"), EnableRateLimiting("public-read")]
    public async Task<IActionResult> Cita(string token, CancellationToken ct = default)
    {
        var reserva = await BuscarCita(token, ct);
        if (reserva?.EmpresaId is not int empresaId) return NotFound();
        await using var tenant = Contexto(empresaId);
        return Ok(await Confirmacion(tenant, reserva.CitaId, ct));
    }

    [HttpPost("cita/{token}/cancelar"), EnableRateLimiting("public-book")]
    public async Task<IActionResult> Cancelar(string token, CancellationToken ct = default)
    {
        var reserva = await BuscarCita(token, ct);
        if (reserva?.EmpresaId is not int empresaId) return NotFound();
        await using var tenant = Contexto(empresaId);
        await using var transaction = await TransaccionAgenda.Abrir(tenant, ct);
        var cita = await tenant.Citas.SingleAsync(c => c.Id == reserva.CitaId, ct);
        if (cita.Estado == EstadoCita.Cancelada) return Ok(await Confirmacion(tenant, cita.Id, ct));
        if (cita.Estado != EstadoCita.Pendiente || cita.Fecha <= DateTime.UtcNow)
            return Conflict("Ya no puedes cancelar desde este enlace. Contacta con el negocio.");
        cita.Estado = EstadoCita.Cancelada;
        await tenant.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await AgendaNotificationFilter.Notificar(hub, logger, empresaId);
        return Ok(await Confirmacion(tenant, cita.Id, ct));
    }

    private Task<ReservaPublica?> BuscarCita(string token, CancellationToken ct) => TokenValido(token)
        ? db.ReservasPublicas.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == Hash(token), ct)
        : Task.FromResult<ReservaPublica?>(null);

    private static async Task<ConfirmacionReservaDto> Confirmacion(AppDbContext tenant, int citaId, CancellationToken ct)
    {
        var reserva = await tenant.ReservasPublicas.AsNoTracking().Include(r => r.Cita).ThenInclude(c => c.Empleado)
            .Include(r => r.Cita).ThenInclude(c => c.CitaServicios).ThenInclude(s => s.Servicio)
            .SingleAsync(r => r.CitaId == citaId, ct);
        var perfil = await tenant.PerfilesPublicos.AsNoTracking().SingleAsync(ct);
        var cita = reserva.Cita;
        return new(perfil.Nombre, cita.Empleado.Nombre, cita.Empleado.ZonaHorariaAgenda ?? "UTC", reserva.NombreCliente,
            DateTime.SpecifyKind(cita.Fecha, DateTimeKind.Utc), cita.Estado.ToString(), cita.Estado == EstadoCita.Pendiente && cita.Fecha > DateTime.UtcNow,
            reserva.NombreLocal, reserva.DireccionLocal, perfil.TelefonoPublico,
            cita.CitaServicios.Select(s => new ServicioPublicoDto(s.ServicioId, s.Servicio.Nombre,
                (s.DuracionMinutos ?? s.Servicio.DuracionMinutos) * s.Cantidad, s.Precio * s.Cantidad)).ToList());
    }
}
