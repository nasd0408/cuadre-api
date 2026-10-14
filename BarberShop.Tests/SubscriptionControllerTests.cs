using BarberShop.Controllers;
using BarberShop.Enum;
using BarberShop.Helpers;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

public class SubscriptionControllerTests
{
    [Fact]
    public async Task Gracia_vencida_baja_a_Gratis_sin_bloquear_hasta_el_job_nocturno()
    {
        var user = new UserContextFalso { EmpresaId = 1 };
        await using var db = Ayudas.CrearContexto(user, "subscription-downgrade-inmediato");

        db.Empresas.Add(new Empresa
        {
            Id = 1,
            Nombre = "Negocio",
            Telefono = "",
            Direccion = "",
            Estado = EstadoSuscripcion.Moroso,
            FechaVencimiento = DateTime.UtcNow.AddDays(-6),
            FechaBloqueo = DateTime.UtcNow.AddDays(-1),
        });
        db.EmpresaConfigs.Add(PlanDefaults.CrearConfig(1, PlanEmpresa.Profesional));
        await db.SaveChangesAsync();

        var controller = new SubscriptionController(db, user);
        var resultado = Assert.IsType<OkObjectResult>(await controller.GetStatus());
        var respuesta = Assert.IsType<SubscriptionStatusResponse>(resultado.Value);

        Assert.Equal(nameof(EstadoSuscripcion.Activo), respuesta.Estado);
        Assert.True(respuesta.PuedeOperar);

        var config = await db.EmpresaConfigs.SingleAsync();
        var empresa = await db.Empresas.FindAsync(1);
        Assert.NotNull(config);
        Assert.Equal(PlanEmpresa.Gratis, config!.Plan);
        Assert.True(config.MostrarAnuncios);
        Assert.False(config.AccesoWeb);
        Assert.Null(empresa!.FechaVencimiento);
        Assert.Null(empresa.FechaBloqueo);
    }

    [Fact]
    public async Task Suspension_manual_no_se_convierte_en_Gratis()
    {
        var user = new UserContextFalso { EmpresaId = 2 };
        await using var db = Ayudas.CrearContexto(user, "subscription-suspension-manual");

        db.Empresas.Add(new Empresa
        {
            Id = 2,
            Nombre = "Suspendido",
            Telefono = "",
            Direccion = "",
            Estado = EstadoSuscripcion.Suspendido,
            FechaBloqueo = DateTime.UtcNow.AddDays(-1),
        });
        db.EmpresaConfigs.Add(PlanDefaults.CrearConfig(2, PlanEmpresa.Premium));
        await db.SaveChangesAsync();

        var controller = new SubscriptionController(db, user);
        var resultado = Assert.IsType<OkObjectResult>(await controller.GetStatus());
        var respuesta = Assert.IsType<SubscriptionStatusResponse>(resultado.Value);

        Assert.Equal(nameof(EstadoSuscripcion.Suspendido), respuesta.Estado);
        Assert.False(respuesta.PuedeOperar);
        Assert.Equal(PlanEmpresa.Premium, (await db.EmpresaConfigs.SingleAsync()).Plan);
    }
}
