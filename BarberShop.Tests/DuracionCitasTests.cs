using BarberShop.Controllers;
using BarberShop.Dtos;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

public class DuracionCitasTests
{
    [Fact]
    public async Task Cambiar_catalogo_no_extiende_citas_y_reprogramar_conserva_duracion()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 });
        db.AddRange(new Empleado { Id = 1, Nombre = "Ana", ZonaHorariaAgenda = "UTC", HorarioAgendaActivo = true },
            new Cliente { Id = 1, Nombre = "Cliente" },
            new Servicio { Id = 1, Nombre = "Corte", Categoria = "Cabello", DuracionMinutos = 30 });
        db.TramosHorario.Add(new TramoHorario { EmpleadoId = 1, DiaSemana = 1, InicioMinuto = 540, FinMinuto = 600 });
        await db.SaveChangesAsync();
        var inicio = new DateTime(2030, 1, 7, 9, 0, 0, DateTimeKind.Utc);
        var api = new CitasController(db);
        var created = Assert.IsType<CitaResponseDto>(Assert.IsType<OkObjectResult>(await api.AgendarCita(new CreateCitaBaseDto
        { EmpleadoId = 1, ClienteId = 1, Fecha = inicio, Servicios = [new() { ServicioId = 1, Cantidad = 1 }] })).Value);
        (await db.Servicios.SingleAsync()).DuracionMinutos = 90;
        await db.SaveChangesAsync();
        var disponibilidad = Assert.IsType<DisponibilidadDto>(Assert.IsType<OkObjectResult>(await new HorariosController(db).Disponibilidad(1, new DateOnly(2030, 1, 7))).Value);
        Assert.Equal(inicio.AddMinutes(30), Assert.Single(disponibilidad.IniciosUtc));
        var updated = Assert.IsType<CitaResponseDto>(Assert.IsType<OkObjectResult>(await api.UpdateCita(created.Id, new UpdateCitaDto
        { EmpleadoId = 1, ClienteId = 1, Fecha = inicio.AddMinutes(30), Servicios = [new() { ServicioId = 1, Cantidad = 1 }] })).Value);
        Assert.Equal(30, Assert.Single(updated.Servicios).DuracionMinutos);
        Assert.Equal(30, (await db.CitasServicios.SingleAsync()).DuracionMinutos);
    }
}
