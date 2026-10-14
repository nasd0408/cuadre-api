using BarberShop.Controllers;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarberShop.Tests;

/// <summary>
/// El aviso CS8601 del compilador apuntaba a un fallo real: AjustesEmpresaDto
/// declara Telefono y Direccion como opcionales, pero las columnas son NOT NULL.
/// Un usuario que borrara el telefono de su negocio recibia un 500.
/// </summary>
public class AjustesEmpresaTests
{
    [Fact]
    public async Task Borrar_el_telefono_del_negocio_lo_guarda_vacio_y_no_revienta()
    {
        var db = Ayudas.CrearContexto(
            new UserContextFalso { EmpresaId = 1 },
            nameof(Borrar_el_telefono_del_negocio_lo_guarda_vacio_y_no_revienta));

        db.Empresas.Add(new Empresa
        {
            Id = 1,
            Nombre = "Negocio",
            Telefono = "0412-1112233",
            Direccion = "Calle 1",
            Estado = EstadoSuscripcion.Activo,
        });
        await db.SaveChangesAsync();

        var api = new AjustesController(
            db,
            new UserContextFalso { EmpresaId = 1 },
            null!,
            new PlanValidationService(db, Ayudas.AnunciosPorDefecto()),
            NullLogger<AjustesController>.Instance);

        // Lo que manda la app cuando el usuario vacia el campo.
        await api.UpdateEmpresa(new AjustesEmpresaDto
        {
            Nombre = "Negocio",
            Telefono = null,
            Direccion = null,
        });

        var empresa = await db.Empresas.SingleAsync();
        Assert.Equal(string.Empty, empresa.Telefono);
        Assert.Equal(string.Empty, empresa.Direccion);
    }
}
