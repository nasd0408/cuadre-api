using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Helpers;
using BarberShop.Models;
using BarberShop.Services;

namespace BarberShop.Tests;

/// <summary>
/// Límites y módulos por plan. Es lo que separa Gratis de lo pagado, así que un
/// error aquí regala funciones o bloquea a quien sí pagó.
/// </summary>
public class PlanValidationServiceTests
{
    private const int EMPRESA = 1;

    private static (AppDbContext db, PlanValidationService servicio) Preparar(
        PlanEmpresa plan,
        string nombreBd)
    {
        var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = EMPRESA }, nombreBd);
        db.EmpresaConfigs.Add(PlanDefaults.CrearConfig(EMPRESA, plan));
        db.SaveChanges();

        return (db, new PlanValidationService(db, Ayudas.AnunciosPorDefecto()));
    }

    [Fact]
    public async Task Gratis_bloquea_al_llegar_al_tope_de_servicios()
    {
        var (db, servicio) = Preparar(PlanEmpresa.Gratis, "plan-servicios");
        var max = PlanDefaults.CrearConfig(EMPRESA, PlanEmpresa.Gratis).MaxServicios;

        for (var i = 0; i < max; i++)
        {
            db.Servicios.Add(new Servicio
            {
                Nombre = $"Servicio {i}",
                Precio = 10,
                Activo = true,
                Categoria = "Otro",
            });
        }
        await db.SaveChangesAsync();

        var (permitido, error) = await servicio.PuedeCrearServicioAsync(EMPRESA);

        Assert.False(permitido);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Gratis_permite_crear_mientras_haya_cupo()
    {
        var (db, servicio) = Preparar(PlanEmpresa.Gratis, "plan-servicios-cupo");
        db.Servicios.Add(new Servicio
        {
            Nombre = "Único",
            Precio = 10,
            Activo = true,
            Categoria = "Otro",
        });
        await db.SaveChangesAsync();

        var (permitido, _) = await servicio.PuedeCrearServicioAsync(EMPRESA);

        Assert.True(permitido);
    }

    [Fact]
    public async Task Premium_no_tiene_tope_de_empleados()
    {
        var (db, servicio) = Preparar(PlanEmpresa.Premium, "plan-empleados-premium");
        for (var i = 0; i < 50; i++)
        {
            db.Empleados.Add(new Empleado { Nombre = $"Empleado {i}", Activo = true });
        }
        await db.SaveChangesAsync();

        var (permitido, _) = await servicio.PuedeCrearEmpleadoAsync(EMPRESA);

        Assert.True(permitido);
    }

    [Theory]
    [InlineData("reportes")]
    [InlineData("recordatorios")]
    [InlineData("calendario")]
    [InlineData("pagos")]
    public async Task Gratis_no_tiene_ningun_modulo_premium(string modulo)
    {
        var (_, servicio) = Preparar(PlanEmpresa.Gratis, $"plan-modulo-{modulo}");

        var (permitido, _) = await servicio.TieneModuloAsync(EMPRESA, modulo);

        Assert.False(permitido);
    }

    [Fact]
    public async Task Premium_tiene_pagos_que_es_exclusivo_de_ese_plan()
    {
        var (_, servicio) = Preparar(PlanEmpresa.Premium, "plan-modulo-pagos-premium");

        var (permitido, _) = await servicio.TieneModuloAsync(EMPRESA, "pagos");

        Assert.True(permitido);
    }

    /// <summary>
    /// La app decide si pinta anuncios y si deja entrar por web a partir de estos
    /// campos. Si dejan de viajar en el DTO, el cliente falla cerrado y el plan Gratis
    /// deja de monetizar en silencio.
    /// </summary>
    [Fact]
    public async Task El_plan_informa_a_la_app_sobre_anuncios_y_acceso_web()
    {
        var (_, servicio) = Preparar(PlanEmpresa.Gratis, "plan-info-gratis");

        var info = await servicio.GetPlanInfoAsync(EMPRESA);

        Assert.NotNull(info);
        Assert.Equal("Gratis", info!.PlanNombre);
        Assert.True(info.MostrarAnuncios);
        Assert.False(info.AccesoWeb);
        Assert.NotNull(info.Anuncios);
    }

    [Fact]
    public async Task Un_plan_pago_informa_que_no_lleva_anuncios()
    {
        var (_, servicio) = Preparar(PlanEmpresa.Profesional, "plan-info-pro");

        var info = await servicio.GetPlanInfoAsync(EMPRESA);

        Assert.NotNull(info);
        Assert.False(info!.MostrarAnuncios);
        Assert.True(info.AccesoWeb);
    }

    [Fact]
    public async Task Una_empresa_sin_configuracion_no_puede_crear_nada()
    {
        var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = EMPRESA }, "plan-sin-config");
        var servicio = new PlanValidationService(db, Ayudas.AnunciosPorDefecto());

        var (permitido, error) = await servicio.PuedeCrearServicioAsync(EMPRESA);

        Assert.False(permitido);
        Assert.NotNull(error);
    }
}
