using System.Diagnostics;
using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BarberShop.Tests;

public sealed class PostgresAgendaFactAttribute : FactAttribute
{
    public PostgresAgendaFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADRE_TEST_POSTGRES")))
            Skip = "Requiere CUADRE_TEST_POSTGRES apuntando a un PostgreSQL local de pruebas.";
    }
}

// Cada prueba crea y destruye una BD propia: nunca utiliza el esquema de la app.
public class AgendaPostgresTests : IAsyncLifetime
{
    private string conexion = "";
    private string administracion = "";
    private readonly string database = "cuadre_test_" + Guid.NewGuid().ToString("N");
    private static readonly DateTime Inicio = new(2030, 1, 7, 9, 0, 0, DateTimeKind.Utc);

    private AppDbContext Db(int empresaId = 1) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conexion).Options,
        new UserContextFalso { EmpresaId = empresaId });

    public async Task InitializeAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("CUADRE_TEST_POSTGRES"));
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || builder.Database?.StartsWith("cuadre_test_", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Solo se admiten PostgreSQL local y Database=cuadre_test_*.");
        builder.Pooling = false;
        builder.Database = "postgres";
        administracion = builder.ConnectionString;
        await using (var admin = new NpgsqlConnection(administracion))
        {
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await command.ExecuteNonQueryAsync();
        }
        builder.Database = database;
        conexion = builder.ConnectionString;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        db.Empresas.AddRange(new Empresa { Id = 1, Nombre = "Empresa A" }, new Empresa { Id = 2, Nombre = "Empresa B" });
        db.AddRange(new Empleado { Id = 1, EmpresaId = 1, Nombre = "Ana", ZonaHorariaAgenda = "UTC" },
            // Dejar que PostgreSQL avance la secuencia: las reservas públicas crean clientes.
            new Cliente { EmpresaId = 1, Nombre = "Cliente" },
            new Servicio { Id = 1, EmpresaId = 1, Nombre = "Corte", Categoria = "Cabello", DuracionMinutos = 30, Precio = 15 });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(administracion)) return;
        await using var admin = new NpgsqlConnection(administracion);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }

    private static CreateCitaBaseDto Reserva(DateTime? inicio = null) => new()
    { ClienteId = 1, EmpleadoId = 1, Fecha = inicio ?? Inicio, Servicios = [new() { ServicioId = 1, Cantidad = 1 }] };

    private async Task<DateTime> PrepararPublico()
    {
        await using var db = Db();
        var inicio = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(2).AddHours(9), DateTimeKind.Utc);
        var empleado = await db.Empleados.SingleAsync(); empleado.HorarioAgendaActivo = true;
        db.AddRange(new Sucursal { Id = 1, EmpresaId = 1, Nombre = "Centro", Publicada = true },
            new PerfilPublicoEmpresa { EmpresaId = 1, Nombre = "Salón", Categoria = "Belleza", Publicado = true,
                ReservasActivas = true, TokenReservas = ReservasPublicasTests.TokenNegocio, SucursalReservasId = 1 },
            new TramoHorario { EmpresaId = 1, EmpleadoId = 1, DiaSemana = (int)inicio.DayOfWeek, InicioMinuto = 540, FinMinuto = 720 });
        await db.SaveChangesAsync(); return inicio;
    }

    private ReservasPublicasController Publico(AppDbContext db, IServiceProvider provider) => new(db,
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(conexion).Options,
        ReservasPublicasTests.Hub(provider), NullLogger<ReservasPublicasController>.Instance);

    [PostgresAgendaFact]
    public async Task Reserva_publica_y_reserva_interna_simultaneas_solo_ocupan_el_hueco_una_vez()
    {
        var inicio = await PrepararPublico(); using var provider = ReservasPublicasTests.Servicios();
        await using var control = Db(); await using var bloqueo = await TransaccionAgenda.Abrir(control);
        await using var a = Db(); await using var b = Db();
        var publica = Publico(a, provider).Crear(ReservasPublicasTests.TokenNegocio, ReservasPublicasTests.Solicitud(inicio));
        var interna = new CitasController(b).AgendarCita(Reserva(inicio));
        await EsperarBloqueadas(2); await bloqueo.CommitAsync();
        var resultados = await Task.WhenAll(publica, interna);
        Assert.Single(resultados.OfType<ConflictObjectResult>());
        Assert.Single(resultados.OfType<ObjectResult>(), r => r.StatusCode is 200 or 201);
        await using var verificar = Db(); Assert.Equal(1, await verificar.Citas.CountAsync());
        Assert.Equal(1 + await verificar.ReservasPublicas.CountAsync(), await verificar.Clientes.CountAsync());
    }

    [PostgresAgendaFact]
    public async Task Reintentos_publicos_simultaneos_crean_una_sola_cita_y_un_solo_cliente()
    {
        var inicio = await PrepararPublico(); using var provider = ReservasPublicasTests.Servicios();
        await using var control = Db(); await using var bloqueo = await TransaccionAgenda.Abrir(control);
        await using var a = Db(); await using var b = Db();
        var primera = Publico(a, provider).Crear(ReservasPublicasTests.TokenNegocio, ReservasPublicasTests.Solicitud(inicio));
        var segunda = Publico(b, provider).Crear(ReservasPublicasTests.TokenNegocio, ReservasPublicasTests.Solicitud(inicio));
        await EsperarBloqueadas(2); await bloqueo.CommitAsync();
        var resultados = await Task.WhenAll(primera, segunda);
        Assert.Equal(new int?[] { 200, 201 }, resultados.Cast<ObjectResult>().Select(r => r.StatusCode).Order().ToArray());
        await using var verificar = Db(); Assert.Equal(1, await verificar.Citas.CountAsync());
        Assert.Equal(1, await verificar.ReservasPublicas.CountAsync()); Assert.Equal(2, await verificar.Clientes.CountAsync());
    }

    [PostgresAgendaFact]
    public async Task Reserva_publica_y_ausencia_simultaneas_no_se_solapan()
    {
        var inicio = await PrepararPublico(); using var provider = ReservasPublicasTests.Servicios();
        await using var control = Db(); await using var bloqueo = await TransaccionAgenda.Abrir(control);
        await using var a = Db(); await using var b = Db();
        var reserva = Publico(a, provider).Crear(ReservasPublicasTests.TokenNegocio, ReservasPublicasTests.Solicitud(inicio));
        var ausencia = new AusenciasController(b).Create(1, new CrearAusenciaDto { Desde = DateOnly.FromDateTime(inicio), Hasta = DateOnly.FromDateTime(inicio) });
        await EsperarBloqueadas(2); await bloqueo.CommitAsync();
        var resultados = await Task.WhenAll(reserva, ausencia);
        Assert.Single(resultados.OfType<ConflictObjectResult>());
        await using var verificar = Db(); Assert.Equal(1, await verificar.Citas.CountAsync() + await verificar.AusenciasEmpleados.CountAsync());
    }

    private async Task EsperarBloqueadas(int cantidad)
    {
        await using var connection = new NpgsqlConnection(conexion);
        await connection.OpenAsync();
        var reloj = Stopwatch.StartNew();
        while (reloj.Elapsed < TimeSpan.FromSeconds(5))
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())", connection);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= cantidad) return;
            await Task.Delay(20);
        }
        Assert.Fail("Las peticiones no esperaron al bloqueo de PostgreSQL.");
    }

    [PostgresAgendaFact]
    public async Task Dos_reservas_simultaneas_solo_confirman_una()
    {
        await using var control = Db();
        await using var bloqueo = await TransaccionAgenda.Abrir(control);
        await using var a = Db(); await using var b = Db();
        var primera = new CitasController(a).AgendarCita(Reserva());
        var segunda = new CitasController(b).AgendarCita(Reserva());
        await EsperarBloqueadas(2);
        await bloqueo.CommitAsync();
        var resultados = await Task.WhenAll(primera, segunda);
        Assert.Single(resultados.OfType<OkObjectResult>());
        Assert.Single(resultados.OfType<ConflictObjectResult>());
        await using var verificar = Db();
        Assert.Equal(1, await verificar.Citas.CountAsync());
    }

    [PostgresAgendaFact]
    public async Task Reserva_y_ausencia_simultaneas_no_pueden_confirmarse_ambas()
    {
        await using var control = Db();
        await using var bloqueo = await TransaccionAgenda.Abrir(control);
        await using var a = Db(); await using var b = Db();
        var reserva = new CitasController(a).AgendarCita(Reserva());
        var ausencia = new AusenciasController(b).Create(1, new CrearAusenciaDto { Desde = DateOnly.FromDateTime(Inicio), Hasta = DateOnly.FromDateTime(Inicio) });
        await EsperarBloqueadas(2);
        await bloqueo.CommitAsync();
        var resultados = await Task.WhenAll(reserva, ausencia);
        Assert.Single(resultados.OfType<ConflictObjectResult>());
        await using var verificar = Db();
        Assert.Equal(1, await verificar.Citas.CountAsync() + await verificar.AusenciasEmpleados.CountAsync());
    }

    [PostgresAgendaFact]
    public async Task Reprogramar_y_reservar_el_mismo_hueco_solo_permite_una_operacion()
    {
        int id;
        await using (var seed = Db())
        {
            var resultado = Assert.IsType<OkObjectResult>(await new CitasController(seed).AgendarCita(Reserva(Inicio.AddHours(2))));
            id = Assert.IsType<CitaResponseDto>(resultado.Value).Id;
        }
        await using var control = Db();
        await using var bloqueo = await TransaccionAgenda.Abrir(control);
        await using var a = Db(); await using var b = Db();
        var editar = new CitasController(a).UpdateCita(id, new UpdateCitaDto { ClienteId = 1, EmpleadoId = 1, Fecha = Inicio, Servicios = [new() { ServicioId = 1, Cantidad = 1 }] });
        var crear = new CitasController(b).AgendarCita(Reserva());
        await EsperarBloqueadas(2);
        await bloqueo.CommitAsync();
        var resultados = await Task.WhenAll(editar, crear);
        Assert.Single(resultados.OfType<OkObjectResult>());
        Assert.Single(resultados.OfType<ConflictObjectResult>());
        await using var verificar = Db();
        Assert.Equal(1, await verificar.Citas.CountAsync(c => c.Fecha == Inicio));
    }

    [PostgresAgendaFact]
    public async Task Rollback_revierte_escrituras_y_libera_el_bloqueo()
    {
        await using (var db = Db())
        await using (var transaction = await TransaccionAgenda.Abrir(db))
        {
            db.AusenciasEmpleados.Add(new AusenciaEmpleado { EmpleadoId = 1, InicioUtc = Inicio, FinUtc = Inicio.AddDays(1), Desde = DateOnly.FromDateTime(Inicio), Hasta = DateOnly.FromDateTime(Inicio) });
            await db.SaveChangesAsync();
        }
        await using var verificar = Db();
        Assert.Empty(await verificar.AusenciasEmpleados.ToListAsync());
        Assert.IsType<OkObjectResult>(await new CitasController(verificar).AgendarCita(Reserva()));
    }

    [PostgresAgendaFact]
    public async Task Empresas_distintas_no_se_bloquean_entre_si()
    {
        await using var a = Db(1); await using var b = Db(2);
        await using var primero = await TransaccionAgenda.Abrir(a);
        await using var segundo = await TransaccionAgenda.Abrir(b).WaitAsync(TimeSpan.FromSeconds(3));
        await segundo.CommitAsync();
    }
}
