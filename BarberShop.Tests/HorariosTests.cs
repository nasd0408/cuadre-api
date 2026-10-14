using System.ComponentModel.DataAnnotations;
using BarberShop.Controllers;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

public class HorariosTests
{
    [Theory]
    [InlineData(9, 0, 60, true)]
    [InlineData(11, 30, 30, true)]
    [InlineData(11, 30, 31, false)]
    [InlineData(12, 0, 30, false)]
    [InlineData(8, 45, 30, false)]
    [InlineData(14, 0, 60, true)]
    public void Respeta_turnos_descansos_y_limites(int hora, int minuto, int duracion, bool esperado)
    {
        TramoHorario[] tramos = [new() { DiaSemana = 1, InicioMinuto = 540, FinMinuto = 720 }, new() { DiaSemana = 1, InicioMinuto = 840, FinMinuto = 1080 }];
        Assert.Equal(esperado, HorarioService.Contiene(new DateTime(2030, 1, 7, hora, minuto, 0, DateTimeKind.Utc), duracion, TimeZoneInfo.Utc, tramos));
    }

    [Fact]
    public void Omite_horas_inexistentes_y_ambiguas_de_Canarias()
    {
        var zona = TimeZoneInfo.FindSystemTimeZoneById("Atlantic/Canary");
        Assert.Null(HorarioService.AUTC(new DateTime(2026, 3, 29, 1, 30, 0), zona));
        Assert.Null(HorarioService.AUTC(new DateTime(2026, 10, 25, 1, 30, 0), zona));
        Assert.Equal(new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc), HorarioService.AUTC(new DateTime(2026, 7, 1, 9, 0, 0), zona));
    }

    [Fact]
    public void Valida_solapamientos_y_zona()
    {
        var dto = new HorarioDto { ZonaHoraria = "NoExiste", Tramos = [new() { DiaSemana = 1, InicioMinuto = 600, FinMinuto = 720 }, new() { DiaSemana = 1, InicioMinuto = 700, FinMinuto = 800 }] };
        var errores = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(dto, new ValidationContext(dto), errores, true));
        Assert.Equal(2, errores.Count);
    }

    [Fact]
    public async Task No_edita_ni_consulta_horarios_de_otra_empresa()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        db.Empleados.Add(new Empleado { Id = 9, EmpresaId = 2, Nombre = "Ajeno" });
        await db.SaveChangesAsync();
        var api = new HorariosController(db);
        Assert.IsType<NotFoundResult>(await api.Get(9));
        Assert.IsType<NotFoundResult>(await api.Put(9, new HorarioDto()));
        Assert.IsType<NotFoundResult>(await api.Disponibilidad(9, new DateOnly(2030, 1, 7)));
        Assert.Empty(await db.TramosHorario.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Desactivar_conserva_zona_y_turnos_y_no_restringe_la_agenda()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        db.Empleados.Add(new Empleado { Id = 1, Nombre = "Ana" });
        await db.SaveChangesAsync();
        var api = new HorariosController(db);
        var dto = new HorarioDto { Activo = false, ZonaHoraria = "Atlantic/Canary", Tramos = [new() { DiaSemana = 1, InicioMinuto = 540, FinMinuto = 720 }] };
        var result = Assert.IsType<HorarioDto>(Assert.IsType<OkObjectResult>(await api.Put(1, dto)).Value);
        Assert.Equal(dto.ZonaHoraria, result.ZonaHoraria);
        Assert.Single(result.Tramos);
        Assert.True(await new HorarioService(db).Permite(1, new DateTime(2030, 1, 7, 23, 0, 0), 30));
    }

    [Fact]
    public async Task Disponibilidad_descontabiliza_citas_y_agendar_rechaza_fuera_de_turno()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        db.AddRange(new Empleado { Id = 1, Nombre = "Ana", HorarioAgendaActivo = true, ZonaHorariaAgenda = "UTC" },
            new Cliente { Id = 1, Nombre = "Cliente" },
            new Servicio { Id = 1, Nombre = "Corte", Categoria = "Cabello", DuracionMinutos = 30 });
        db.TramosHorario.Add(new TramoHorario { EmpleadoId = 1, DiaSemana = 1, InicioMinuto = 540, FinMinuto = 600 });
        var cita = new Cita { EmpleadoId = 1, ClienteId = 1, Fecha = new DateTime(2030, 1, 7, 9, 0, 0, DateTimeKind.Utc), Estado = EstadoCita.Pendiente };
        cita.CitaServicios.Add(new CitaServicio { ServicioId = 1, Cantidad = 1 });
        db.Citas.Add(cita);
        await db.SaveChangesAsync();
        var result = Assert.IsType<DisponibilidadDto>(Assert.IsType<OkObjectResult>(await new HorariosController(db).Disponibilidad(1, new DateOnly(2030, 1, 7), 30)).Value);
        Assert.Equal(new DateTime(2030, 1, 7, 9, 30, 0, DateTimeKind.Utc), Assert.Single(result.IniciosUtc));
        var api = new CitasController(db);
        Assert.IsType<ConflictObjectResult>(await api.AgendarCita(new CreateCitaBaseDto
        { EmpleadoId = 1, ClienteId = 1, Fecha = cita.Fecha.AddHours(2), Servicios = [new() { ServicioId = 1, Cantidad = 1 }] }));
        Assert.IsType<ConflictObjectResult>(await api.UpdateCita(cita.Id, new UpdateCitaDto
        { EmpleadoId = 1, ClienteId = 1, Fecha = cita.Fecha.AddHours(2), Servicios = [new() { ServicioId = 1, Cantidad = 1 }] }));
        Assert.Equal(9, cita.Fecha.Hour);
    }
}
