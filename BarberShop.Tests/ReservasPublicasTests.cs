using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Hubs;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarberShop.Tests;

public class ReservasPublicasTests
{
    internal const string TokenNegocio = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    internal static async Task<DateTime> Sembrar(AppDbContext db)
    {
        var inicio = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(2).AddHours(9), DateTimeKind.Utc);
        if (!await db.Empresas.AnyAsync(e => e.Id == 1))
            db.Empresas.Add(new Empresa { Id = 1, Nombre = "Interno", Telefono = "No publicar" });
        db.AddRange(new Sucursal { Id = 1, EmpresaId = 1, Nombre = "Local centro", DireccionPublica = "Calle 1", Publicada = true },
            new PerfilPublicoEmpresa { EmpresaId = 1, Nombre = "Salón público", Categoria = "Belleza", Publicado = true,
                ReservasActivas = true, TokenReservas = TokenNegocio, SucursalReservasId = 1, TelefonoPublico = "+34922123456" },
            new Empleado { Id = 1, EmpresaId = 1, Nombre = "Ana", HorarioAgendaActivo = true, ZonaHorariaAgenda = "UTC", PorcentajeComision = 25 },
            new Empleado { Id = 2, EmpresaId = 2, Nombre = "Ajeno", HorarioAgendaActivo = true, ZonaHorariaAgenda = "UTC" },
            new Empleado { Id = 3, EmpresaId = 1, Nombre = "Sin horario" },
            new Servicio { Id = 1, EmpresaId = 1, Nombre = "Corte", Categoria = "Cabello", Precio = 15, DuracionMinutos = 30 },
            new Servicio { Id = 2, EmpresaId = 2, Nombre = "Ajeno", Categoria = "Cabello", Precio = 500, DuracionMinutos = 30 },
            new TramoHorario { EmpleadoId = 1, EmpresaId = 1, DiaSemana = (int)inicio.DayOfWeek, InicioMinuto = 540, FinMinuto = 720 },
            new Cliente { Id = 1, EmpresaId = 1, Nombre = "Ficha privada", Telefono = "+34600123456" });
        await db.SaveChangesAsync();
        return inicio;
    }

    internal static CrearReservaPublicaDto Solicitud(DateTime inicio, string? token = null) => new()
    { SolicitudToken = token ?? new string('b', 64), EmpleadoId = 1, ServicioId = 1, InicioUtc = new DateTimeOffset(inicio),
        Nombre = "Cliente web", Telefono = "+34 600 123 456", Email = "cliente@example.com" };

    internal static IHubContext<AgendaHub> Hub(IServiceProvider provider) => provider.GetRequiredService<IHubContext<AgendaHub>>();
    internal static ServiceProvider Servicios() => new ServiceCollection().AddLogging().AddSignalR().Services.BuildServiceProvider();

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly DbContextOptions<AppDbContext> Options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)).Options;
        public readonly ServiceProvider Provider = Servicios();
        public AppDbContext Db(int? empresa = 1) => new(Options, new UserContextFalso { EmpresaId = empresa });
        public ReservasPublicasController Api(AppDbContext visitante) => new(visitante, Options, Hub(Provider), NullLogger<ReservasPublicasController>.Instance);
        public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
    }

    [Fact]
    public async Task Reserva_anonima_ignora_empresa_del_visitante_y_no_modifica_fichas_privadas()
    {
        await using var fixture = new Fixture(); await using var owner = fixture.Db();
        var inicio = await Sembrar(owner);
        await using var visitante = fixture.Db(2);
        var api = fixture.Api(visitante);
        var catalogo = Assert.IsType<CatalogoReservasDto>(Assert.IsType<OkObjectResult>(await api.Get(TokenNegocio)).Value);
        Assert.Equal("Ana", Assert.Single(catalogo.Profesionales).Nombre);
        Assert.Equal("Corte", Assert.Single(catalogo.Servicios).Nombre);
        var respuesta = Assert.IsType<ConfirmacionReservaDto>(Assert.IsType<ObjectResult>(await api.Crear(TokenNegocio, Solicitud(inicio))).Value);
        Assert.Equal("Salón público", respuesta.Negocio); Assert.True(respuesta.PuedeCancelar);
        Assert.Equal(DateTimeKind.Utc, respuesta.InicioUtc.Kind);
        var reserva = await owner.ReservasPublicas.Include(r => r.Cita).ThenInclude(c => c.CitaServicios).SingleAsync();
        Assert.Equal(1, reserva.EmpresaId); Assert.NotEqual(1, reserva.Cita.ClienteId);
        Assert.Equal(30, Assert.Single(reserva.Cita.CitaServicios).DuracionMinutos);
        Assert.Equal(15, Assert.Single(reserva.Cita.CitaServicios).Precio);
        Assert.Equal("cliente@example.com", reserva.Email); Assert.NotEqual(Solicitud(inicio).SolicitudToken, reserva.TokenHash);
        Assert.Equal("Ficha privada", (await owner.Clientes.SingleAsync(c => c.Id == 1)).Nombre);
        Assert.Empty(await owner.CuentasCliente.ToListAsync());
        Assert.Empty(await visitante.Citas.ToListAsync());
    }

    [Fact]
    public async Task Reintentar_la_misma_solicitud_devuelve_la_misma_cita_y_cambiar_datos_es_conflicto()
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db();
        var inicio = await Sembrar(db); var api = fixture.Api(db); var dto = Solicitud(inicio);
        Assert.Equal(201, Assert.IsType<ObjectResult>(await api.Crear(TokenNegocio, dto)).StatusCode);
        Assert.IsType<OkObjectResult>(await api.Crear(TokenNegocio, dto));
        dto.Nombre = "Otro nombre";
        Assert.IsType<ConflictObjectResult>(await api.Crear(TokenNegocio, dto));
        Assert.Equal(1, await db.Citas.CountAsync()); Assert.Equal(2, await db.Clientes.CountAsync());
    }

    [Theory]
    [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(3, 1)]
    public async Task Rechaza_profesional_o_servicio_ajeno_y_profesional_sin_horario(int empleado, int servicio)
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db();
        var dto = Solicitud(await Sembrar(db)); dto.EmpleadoId = empleado; dto.ServicioId = servicio;
        Assert.IsType<NotFoundResult>(await fixture.Api(db).Crear(TokenNegocio, dto));
        Assert.Empty(await db.Citas.ToListAsync()); Assert.Equal(1, await db.Clientes.CountAsync());
    }

    [Fact]
    public async Task Rechaza_slot_ocupado_ausencias_y_horas_fuera_de_los_slots_publicados()
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db();
        var inicio = await Sembrar(db); var api = fixture.Api(db);
        await api.Crear(TokenNegocio, Solicitud(inicio));
        Assert.IsType<ConflictObjectResult>(await api.Crear(TokenNegocio, Solicitud(inicio, new string('c', 64))));
        Assert.IsType<ConflictObjectResult>(await api.Crear(TokenNegocio, Solicitud(inicio.AddMinutes(7), new string('d', 64))));
        db.AusenciasEmpleados.Add(new AusenciaEmpleado { EmpleadoId = 1, InicioUtc = inicio.AddMinutes(30), FinUtc = inicio.AddHours(1) });
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await api.Crear(TokenNegocio, Solicitud(inicio.AddMinutes(30), new string('e', 64))));
        var libres = Assert.IsType<DisponibilidadDto>(Assert.IsType<OkObjectResult>(await api.Disponibilidad(TokenNegocio, 1, 1, DateOnly.FromDateTime(inicio))).Value);
        Assert.DoesNotContain(inicio, libres.IniciosUtc); Assert.DoesNotContain(inicio.AddMinutes(30), libres.IniciosUtc);
        Assert.Equal(1, await db.Citas.CountAsync());
    }

    [Fact]
    public async Task No_reserva_perfil_privado_agenda_desactivada_local_oculto_o_empresa_suspendida()
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db();
        var inicio = await Sembrar(db); var api = fixture.Api(db);
        var perfil = await db.PerfilesPublicos.SingleAsync(); var local = await db.Sucursales.SingleAsync(); var empresa = await db.Empresas.SingleAsync();
        perfil.Publicado = false; await db.SaveChangesAsync(); Assert.IsType<NotFoundResult>(await api.Get(TokenNegocio));
        perfil.Publicado = true; perfil.ReservasActivas = false; await db.SaveChangesAsync(); Assert.IsType<NotFoundResult>(await api.Crear(TokenNegocio, Solicitud(inicio)));
        perfil.ReservasActivas = true; local.Publicada = false; await db.SaveChangesAsync(); Assert.IsType<NotFoundResult>(await api.Crear(TokenNegocio, Solicitud(inicio)));
        local.Publicada = true; empresa.Estado = EstadoSuscripcion.Suspendido; await db.SaveChangesAsync(); Assert.IsType<NotFoundResult>(await api.Crear(TokenNegocio, Solicitud(inicio)));
        empresa.Estado = EstadoSuscripcion.Activo; empresa.FechaBloqueo = DateTime.UtcNow.AddMinutes(-1); await db.SaveChangesAsync(); Assert.IsType<NotFoundResult>(await api.Get(TokenNegocio));
        Assert.Empty(await db.Citas.ToListAsync());
    }

    [Fact]
    public async Task Enlace_privado_funciona_con_agenda_cerrada_y_cancelar_libera_el_horario()
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db();
        var inicio = await Sembrar(db); var api = fixture.Api(db); var dto = Solicitud(inicio);
        await api.Crear(TokenNegocio, dto);
        Assert.IsType<NotFoundResult>(await api.Cita(TokenNegocio));
        Assert.IsType<NotFoundResult>(await api.Cancelar(new string('f', 64)));
        var perfil = await db.PerfilesPublicos.SingleAsync(); perfil.ReservasActivas = false; await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await api.Cita(dto.SolicitudToken));
        var resultado = Assert.IsType<ConfirmacionReservaDto>(Assert.IsType<OkObjectResult>(await api.Cancelar(dto.SolicitudToken)).Value);
        Assert.Equal("Cancelada", resultado.Estado); Assert.False(resultado.PuedeCancelar);
        Assert.IsType<OkObjectResult>(await api.Cancelar(dto.SolicitudToken));
        perfil.ReservasActivas = true; await db.SaveChangesAsync();
        var libres = Assert.IsType<DisponibilidadDto>(Assert.IsType<OkObjectResult>(await api.Disponibilidad(TokenNegocio, 1, 1, DateOnly.FromDateTime(inicio))).Value);
        Assert.Contains(inicio, libres.IniciosUtc);
    }

    [Fact]
    public async Task No_cancelar_cita_ya_atendida()
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db();
        var dto = Solicitud(await Sembrar(db)); var api = fixture.Api(db); await api.Crear(TokenNegocio, dto);
        var cita = await db.Citas.SingleAsync(); cita.Estado = EstadoCita.PagoPendiente; await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await api.Cancelar(dto.SolicitudToken));
        await db.Entry(cita).ReloadAsync(); Assert.Equal(EstadoCita.PagoPendiente, cita.Estado);
    }

    [Fact]
    public async Task Configuracion_no_acepta_local_ajeno_y_el_token_permanece_al_desactivar()
    {
        await using var fixture = new Fixture(); await using var db = fixture.Db(); await Sembrar(db);
        db.Sucursales.Add(new Sucursal { Id = 2, EmpresaId = 2, Nombre = "Ajena", Publicada = true }); await db.SaveChangesAsync();
        var api = new ConfiguracionReservasController(db);
        Assert.IsType<BadRequestObjectResult>(await api.Put(new() { Activas = true, SucursalId = 2 }));
        await api.Put(new() { Activas = false }); await api.Put(new() { Activas = true, SucursalId = 1 });
        Assert.Equal(TokenNegocio, (await db.PerfilesPublicos.SingleAsync()).TokenReservas);
    }
}
