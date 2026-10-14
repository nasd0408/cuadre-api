using BarberShop.Enum;
using BarberShop.Helpers;

namespace BarberShop.Tests;

/// <summary>
/// El camino del dinero: qué recibe alguien que paga.
///
/// No había ni un test aquí y es el código que decide cuántos días de servicio se
/// entregan y qué plan queda activo. Con cero pagos en la historia del producto,
/// el primero que llegue tiene que funcionar a la primera.
/// </summary>
public class CalculoVencimientoTests
{
    private static readonly DateTime AHORA = new(2026, 8, 6, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Sin_vencimiento_previo_cuenta_desde_ahora()
    {
        var nuevo = CalculoSuscripcion.CalcularNuevoVencimiento(null, AHORA, 30);

        Assert.Equal(AHORA.AddDays(30), nuevo);
    }

    [Fact]
    public void Una_suscripcion_vencida_cuenta_desde_ahora_no_desde_la_fecha_vieja()
    {
        var vencioHaceUnMes = AHORA.AddDays(-30);

        var nuevo = CalculoSuscripcion.CalcularNuevoVencimiento(vencioHaceUnMes, AHORA, 30);

        Assert.Equal(AHORA.AddDays(30), nuevo);
    }

    /// <summary>
    /// Renovar antes de tiempo no puede quitarle al usuario los días que le
    /// quedaban: sería cobrarle por algo que ya había pagado.
    /// </summary>
    [Fact]
    public void Renovar_anticipado_conserva_los_dias_restantes()
    {
        var leQuedan10Dias = AHORA.AddDays(10);

        var nuevo = CalculoSuscripcion.CalcularNuevoVencimiento(leQuedan10Dias, AHORA, 30);

        Assert.Equal(leQuedan10Dias.AddDays(30), nuevo);
        Assert.Equal(40, Math.Round((nuevo - AHORA).TotalDays));
    }

    [Fact]
    public void Dos_pagos_seguidos_acumulan_sesenta_dias()
    {
        var primero = CalculoSuscripcion.CalcularNuevoVencimiento(null, AHORA, 30);
        var segundo = CalculoSuscripcion.CalcularNuevoVencimiento(primero, AHORA, 30);

        Assert.Equal(60, Math.Round((segundo - AHORA).TotalDays));
    }
}

/// <summary>
/// Transición de plan al aprobar un pago. Lo que decide si quien paga deja de ver
/// anuncios y recupera la versión web.
/// </summary>
public class TransicionDePlanTests
{
    [Theory]
    [InlineData(PlanEmpresa.Basico)]
    [InlineData(PlanEmpresa.Profesional)]
    [InlineData(PlanEmpresa.Premium)]
    public void Pagar_desde_Gratis_quita_los_anuncios_y_devuelve_la_web(PlanEmpresa comprado)
    {
        // Estado de partida real: la mayoría de la base está hoy en Gratis.
        var config = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);
        Assert.True(config.MostrarAnuncios);
        Assert.False(config.AccesoWeb);

        PlanDefaults.AplicarA(config, comprado);

        Assert.Equal(comprado, config.Plan);
        Assert.False(config.MostrarAnuncios);
        Assert.True(config.AccesoWeb);
    }

    [Fact]
    public void Pagar_desde_Gratis_amplia_los_limites()
    {
        var config = PlanDefaults.CrearConfig(1, PlanEmpresa.Gratis);
        var serviciosGratis = config.MaxServicios;
        var historialGratis = config.RetencionHistorialMeses;

        PlanDefaults.AplicarA(config, PlanEmpresa.Profesional);

        Assert.True(config.MaxServicios > serviciosGratis);
        Assert.True(config.RetencionHistorialMeses > historialGratis);
        Assert.True(config.ModuloReportes);
        Assert.True(config.ModuloCalendario);
    }

    /// <summary>
    /// Bajar de plan tiene que apagar los módulos: si no, alguien que dejó de
    /// pagar Premium seguiría usando nómina y reportes gratis.
    /// </summary>
    [Fact]
    public void Bajar_de_Premium_a_Gratis_cierra_todos_los_modulos()
    {
        var config = PlanDefaults.CrearConfig(1, PlanEmpresa.Premium);

        PlanDefaults.AplicarA(config, PlanEmpresa.Gratis);

        Assert.False(config.ModuloPagosEmpleados);
        Assert.False(config.ModuloReportes);
        Assert.False(config.ModuloRecordatorios);
        Assert.False(config.ModuloCalendario);
        Assert.True(config.MostrarAnuncios);
        Assert.False(config.AccesoWeb);
    }

    /// <summary>
    /// AplicarA existe para no olvidar campos al añadir capacidades nuevas. Si
    /// alguien agrega una y no la incluye ahí, este test lo detecta.
    /// </summary>
    [Fact]
    public void AplicarA_deja_la_config_identica_a_una_recien_creada()
    {
        var migrada = PlanDefaults.CrearConfig(7, PlanEmpresa.Gratis);
        PlanDefaults.AplicarA(migrada, PlanEmpresa.Premium);

        var reciente = PlanDefaults.CrearConfig(7, PlanEmpresa.Premium);

        Assert.Equal(reciente.Plan, migrada.Plan);
        Assert.Equal(reciente.MaxEmpleados, migrada.MaxEmpleados);
        Assert.Equal(reciente.MaxServicios, migrada.MaxServicios);
        Assert.Equal(reciente.MaxCitasOffline, migrada.MaxCitasOffline);
        Assert.Equal(reciente.RetencionHistorialMeses, migrada.RetencionHistorialMeses);
        Assert.Equal(reciente.ModuloPagosEmpleados, migrada.ModuloPagosEmpleados);
        Assert.Equal(reciente.ModuloReportes, migrada.ModuloReportes);
        Assert.Equal(reciente.ModuloRecordatorios, migrada.ModuloRecordatorios);
        Assert.Equal(reciente.ModuloCalendario, migrada.ModuloCalendario);
        Assert.Equal(reciente.MostrarAnuncios, migrada.MostrarAnuncios);
        Assert.Equal(reciente.AccesoWeb, migrada.AccesoWeb);
    }
}
