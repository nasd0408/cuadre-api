using System.ComponentModel.DataAnnotations;
using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

public class AusenciasTests
{
    private static async Task<AppDbContext> Preparar()
    {
        var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        db.AddRange(new Empleado { Id = 1, Nombre = "Ana", HorarioAgendaActivo = true, ZonaHorariaAgenda = "UTC" },
            new Cliente { Id = 1, Nombre = "Cliente" },
            new Servicio { Id = 1, Nombre = "Corte", Categoria = "Cabello", DuracionMinutos = 30 });
        db.TramosHorario.Add(new TramoHorario { EmpleadoId = 1, DiaSemana = 1, InicioMinuto = 540, FinMinuto = 600 });
        await db.SaveChangesAsync();
        return db;
    }

    private static CrearAusenciaDto Dia() => new() { Desde = new DateOnly(2030, 1, 7), Hasta = new DateOnly(2030, 1, 7) };

    [Fact]
    public async Task Ausencia_bloquea_huecos_y_reservas_incluso_con_horario_desactivado()
    {
        using var db = await Preparar();
        Assert.IsType<ObjectResult>(await new AusenciasController(db).Create(1, Dia()));
        var result = Assert.IsType<DisponibilidadDto>(Assert.IsType<OkObjectResult>(await new HorariosController(db).Disponibilidad(1, Dia().Desde)).Value);
        Assert.Empty(result.IniciosUtc);
        var inicio = new DateTime(2030, 1, 7, 9, 0, 0, DateTimeKind.Utc);
        Assert.False(await new HorarioService(db).Permite(1, inicio, 30));
        var api = new CitasController(db);
        Assert.IsType<ConflictObjectResult>(await api.AgendarCita(new CreateCitaBaseDto { EmpleadoId = 1, ClienteId = 1, Fecha = inicio, Servicios = [new() { ServicioId = 1, Cantidad = 1 }] }));
        (await db.Empleados.SingleAsync()).HorarioAgendaActivo = false;
        await db.SaveChangesAsync();
        Assert.False(await new HorarioService(db).Permite(1, inicio, 30));
        Assert.True(await new HorarioService(db).Permite(1, inicio.AddDays(1), 30));
    }

    [Fact]
    public async Task Rechaza_ausencia_con_cita_que_empieza_el_dia_anterior()
    {
        using var db = await Preparar();
        var cita = new Cita { EmpleadoId = 1, ClienteId = 1, Fecha = new DateTime(2030, 1, 6, 23, 45, 0, DateTimeKind.Utc), Estado = EstadoCita.Pendiente };
        cita.CitaServicios.Add(new CitaServicio { ServicioId = 1, Cantidad = 1 });
        db.Citas.Add(cita);
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await new AusenciasController(db).Create(1, Dia()));
        Assert.Empty(await db.AusenciasEmpleados.ToListAsync());
    }

    [Fact]
    public async Task Rechaza_solapamiento_y_quitar_ausencia_restaura_huecos()
    {
        using var db = await Preparar();
        var api = new AusenciasController(db);
        await api.Create(1, Dia());
        Assert.IsType<ConflictObjectResult>(await api.Create(1, Dia()));
        var ausencia = await db.AusenciasEmpleados.SingleAsync();
        Assert.IsType<NoContentResult>(await api.Delete(1, ausencia.Id));
        var result = Assert.IsType<DisponibilidadDto>(Assert.IsType<OkObjectResult>(await new HorariosController(db).Disponibilidad(1, Dia().Desde)).Value);
        Assert.NotEmpty(result.IniciosUtc);
    }

    [Fact]
    public async Task Otra_empresa_no_puede_leer_crear_ni_quitar_ausencias()
    {
        using var db = await Preparar();
        db.Empleados.Add(new Empleado { Id = 2, EmpresaId = 2, Nombre = "Ajeno" });
        db.AusenciasEmpleados.Add(new AusenciaEmpleado { EmpleadoId = 2, EmpresaId = 2, InicioUtc = new DateTime(2030, 1, 7), FinUtc = new DateTime(2030, 1, 8) });
        await db.SaveChangesAsync();
        var id = (await db.AusenciasEmpleados.IgnoreQueryFilters().SingleAsync()).Id;
        var api = new AusenciasController(db);
        Assert.IsType<NotFoundResult>(await api.Get(2));
        Assert.IsType<NotFoundResult>(await api.Create(2, Dia()));
        Assert.IsType<NotFoundResult>(await api.Delete(2, id));
        Assert.IsType<NotFoundResult>(await api.Delete(1, id));
        Assert.Single(await db.AusenciasEmpleados.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Congela_los_instantes_y_la_zona_al_crear()
    {
        using var db = await Preparar();
        (await db.Empleados.SingleAsync()).ZonaHorariaAgenda = "Atlantic/Canary";
        await db.SaveChangesAsync();
        var dto = new CrearAusenciaDto { Desde = new DateOnly(2030, 7, 1), Hasta = new DateOnly(2030, 7, 1) };
        await new AusenciasController(db).Create(1, dto);
        var ausencia = await db.AusenciasEmpleados.SingleAsync();
        Assert.Equal(new DateTime(2030, 6, 30, 23, 0, 0, DateTimeKind.Utc), ausencia.InicioUtc);
        Assert.Equal(new DateTime(2030, 7, 1, 23, 0, 0, DateTimeKind.Utc), ausencia.FinUtc);
    }

    [Fact]
    public void No_admite_fechas_invertidas_ni_rangos_excesivos()
    {
        var vacio = new CrearAusenciaDto();
        Assert.False(Validator.TryValidateObject(vacio, new ValidationContext(vacio), [], true));
        var dto = new CrearAusenciaDto { Desde = new DateOnly(2030, 1, 7), Hasta = new DateOnly(2030, 1, 6) };
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
        dto.Hasta = new DateOnly(2032, 1, 1);
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), [], true));
    }
}
