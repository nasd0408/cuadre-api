using BarberShop.Enum;
using BarberShop.Helpers;

namespace BarberShop.Tests;

/// <summary>
/// Reglas del plan Gratis. Son las que sostienen el modelo de negocio: si alguna se
/// invierte por accidente, se le muestran anuncios a quien paga o se regala acceso
/// web a quien no.
/// </summary>
public class PlanDefaultsTests
{
    [Fact]
    public void Gratis_muestra_anuncios_y_no_tiene_acceso_web()
    {
        var config = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);

        Assert.True(config.MostrarAnuncios);
        Assert.False(config.AccesoWeb);
    }

    [Theory]
    [InlineData(PlanEmpresa.Basico)]
    [InlineData(PlanEmpresa.Profesional)]
    [InlineData(PlanEmpresa.Premium)]
    public void Los_planes_pagos_nunca_muestran_anuncios_y_si_tienen_web(PlanEmpresa plan)
    {
        var config = PlanDefaults.CrearConfig(1, plan);

        Assert.False(config.MostrarAnuncios);
        Assert.True(config.AccesoWeb);
    }

    [Fact]
    public void Gratis_es_mas_limitado_que_Basico_para_que_pagar_tenga_sentido()
    {
        var gratis = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);
        var basico = PlanDefaults.CrearConfig(1, PlanEmpresa.Basico);

        Assert.True(gratis.MaxServicios < basico.MaxServicios);
        Assert.True(gratis.RetencionHistorialMeses < basico.RetencionHistorialMeses);
    }

    /// <summary>
    /// Bajar de plan no debe dejar citas encoladas imposibles de sincronizar:
    /// ValidarCitasOfflineAsync rechaza el lote completo si excede el límite.
    /// </summary>
    [Fact]
    public void Gratis_no_reduce_la_cola_offline_respecto_a_Basico()
    {
        var gratis = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);
        var basico = PlanDefaults.CrearConfig(1, PlanEmpresa.Basico);

        Assert.True(gratis.MaxCitasOffline >= basico.MaxCitasOffline);
    }

    /// <summary>
    /// Los valores del enum se persisten como integer en EmpresaConfig.Plan y en
    /// PagoSuscripcion.PlanSolicitado. Reordenarlos o insertar uno en medio cambiaría
    /// el plan de todas las empresas ya guardadas SIN ningún error visible: los
    /// Premium pasarían a ser Profesional, etc. Este test congela los números.
    /// </summary>
    [Fact]
    public void Los_valores_numericos_del_enum_no_pueden_cambiar()
    {
        Assert.Equal(0, (int)PlanEmpresa.Basico);
        Assert.Equal(1, (int)PlanEmpresa.Profesional);
        Assert.Equal(2, (int)PlanEmpresa.Premium);
        Assert.Equal(3, (int)PlanEmpresa.Gratis);
    }

    /// <summary>
    /// Agregar un plan al enum y olvidar su caso en PlanDefaults reventaría en runtime
    /// al registrar una empresa. Este test lo detecta al compilar la suite.
    /// </summary>
    [Fact]
    public void Todos_los_planes_del_enum_tienen_configuracion()
    {
        foreach (PlanEmpresa plan in System.Enum.GetValues<PlanEmpresa>())
        {
            var config = PlanDefaults.CrearConfig(1, plan);
            Assert.Equal(plan, config.Plan);
        }
    }

    [Fact]
    public void Pasar_de_Gratis_a_pago_quita_anuncios_y_habilita_web()
    {
        var config = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);

        PlanDefaults.AplicarA(config, PlanEmpresa.Basico);

        Assert.Equal(PlanEmpresa.Basico, config.Plan);
        Assert.False(config.MostrarAnuncios);
        Assert.True(config.AccesoWeb);
    }

    [Fact]
    public void Pasar_de_pago_a_Gratis_aplica_todos_los_limites_y_monetizacion()
    {
        var config = PlanDefaults.CrearConfig(1, PlanEmpresa.Premium);
        var esperado = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);

        PlanDefaults.AplicarA(config, PlanEmpresa.Gratis);

        Assert.Equal(esperado.Plan, config.Plan);
        Assert.Equal(esperado.MaxEmpleados, config.MaxEmpleados);
        Assert.Equal(esperado.MaxServicios, config.MaxServicios);
        Assert.Equal(esperado.ModuloPagosEmpleados, config.ModuloPagosEmpleados);
        Assert.Equal(esperado.MostrarAnuncios, config.MostrarAnuncios);
        Assert.Equal(esperado.AccesoWeb, config.AccesoWeb);
    }
}
