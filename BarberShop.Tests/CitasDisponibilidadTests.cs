using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

public class CitasDisponibilidadTests
{
    private static async Task<(CitasController controller, AppDbContext db, DateTime inicio)>
        Preparar(string nombreBd)
    {
        var db = Ayudas.CrearContexto(
            new UserContextFalso { EmpresaId = 1 },
            nombreBd);

        var empleado = new Empleado
        {
            EmpresaId = 1,
            Nombre = "Ana",
            Activo = true,
        };
        var cliente = new Cliente
        {
            EmpresaId = 1,
            Nombre = "Cliente existente",
        };
        var servicio = new Servicio
        {
            EmpresaId = 1,
            Nombre = "Corte",
            Categoria = "Cabello",
            Precio = 15,
            DuracionMinutos = 30,
            Activo = true,
        };

        db.AddRange(empleado, cliente, servicio);
        await db.SaveChangesAsync();

        var inicio = DateTime.UtcNow.AddHours(2);
        var existente = new Cita
        {
            EmpresaId = 1,
            EmpleadoId = empleado.Id,
            ClienteId = cliente.Id,
            Fecha = inicio,
            Estado = EstadoCita.Pendiente,
        };
        existente.CitaServicios.Add(new CitaServicio
        {
            EmpresaId = 1,
            ServicioId = servicio.Id,
            Servicio = servicio,
            Cantidad = 1,
            Precio = servicio.Precio,
        });
        db.Citas.Add(existente);
        await db.SaveChangesAsync();

        return (new CitasController(db), db, inicio);
    }

    [Fact]
    public async Task Agendar_rechaza_cita_que_se_solapa_con_el_mismo_empleado()
    {
        var (controller, db, inicio) = await Preparar(nameof(
            Agendar_rechaza_cita_que_se_solapa_con_el_mismo_empleado));

        var cliente = new Cliente { EmpresaId = 1, Nombre = "Otro cliente" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        var servicioId = await db.Servicios.Select(s => s.Id).SingleAsync();
        var empleadoId = await db.Empleados.Select(e => e.Id).SingleAsync();

        var resultado = await controller.AgendarCita(new CreateCitaBaseDto
        {
            ClienteId = cliente.Id,
            EmpleadoId = empleadoId,
            Fecha = inicio.AddMinutes(15),
            Servicios = [new ServicioCantidadDto { ServicioId = servicioId, Cantidad = 1 }],
        });

        var conflicto = Assert.IsType<ConflictObjectResult>(resultado);
        Assert.Equal(
            "El empleado ya tiene una cita que coincide con ese horario.",
            conflicto.Value);
        Assert.Equal(1, await db.Citas.CountAsync());
    }

    [Fact]
    public async Task Agendar_permite_cita_justo_al_terminar_la_anterior()
    {
        var (controller, db, inicio) = await Preparar(nameof(
            Agendar_permite_cita_justo_al_terminar_la_anterior));

        var cliente = new Cliente { EmpresaId = 1, Nombre = "Otro cliente" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        var servicioId = await db.Servicios.Select(s => s.Id).SingleAsync();
        var empleadoId = await db.Empleados.Select(e => e.Id).SingleAsync();

        var resultado = await controller.AgendarCita(new CreateCitaBaseDto
        {
            ClienteId = cliente.Id,
            EmpleadoId = empleadoId,
            Fecha = inicio.AddMinutes(30),
            Servicios = [new ServicioCantidadDto { ServicioId = servicioId, Cantidad = 1 }],
        });

        Assert.IsType<OkObjectResult>(resultado);
        Assert.Equal(2, await db.Citas.CountAsync());
    }

    [Fact]
    public async Task Reprogramar_rechaza_solapamiento_sin_modificar_la_cita()
    {
        var (controller, db, inicio) = await Preparar(nameof(
            Reprogramar_rechaza_solapamiento_sin_modificar_la_cita));

        var cliente = new Cliente { EmpresaId = 1, Nombre = "Otro cliente" };
        db.Clientes.Add(cliente);
        await db.SaveChangesAsync();
        var servicio = await db.Servicios.SingleAsync();
        var empleadoId = await db.Empleados.Select(e => e.Id).SingleAsync();
        var fechaOriginal = inicio.AddHours(2);
        var reprogramable = new Cita
        {
            EmpresaId = 1,
            EmpleadoId = empleadoId,
            ClienteId = cliente.Id,
            Fecha = fechaOriginal,
            Estado = EstadoCita.Pendiente,
        };
        reprogramable.CitaServicios.Add(new CitaServicio
        {
            EmpresaId = 1,
            ServicioId = servicio.Id,
            Servicio = servicio,
            Cantidad = 1,
            Precio = servicio.Precio,
        });
        db.Citas.Add(reprogramable);
        await db.SaveChangesAsync();

        var resultado = await controller.UpdateCita(reprogramable.Id, new UpdateCitaDto
        {
            ClienteId = cliente.Id,
            EmpleadoId = empleadoId,
            Fecha = inicio.AddMinutes(10),
            Servicios = [new ServicioCantidadDto { ServicioId = servicio.Id, Cantidad = 1 }],
        });

        Assert.IsType<ConflictObjectResult>(resultado);
        Assert.Equal(fechaOriginal, reprogramable.Fecha);
    }
}
