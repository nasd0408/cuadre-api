using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BarberShop.Tests;

/// <summary>No envía nada: los tests no deben depender de red ni de Expo.</summary>
public class PushFalso : IPushNotificationService
{
    public Task<PreferenciaNotificacion> GetPreferenciasAsync(int empresaId, CancellationToken ct = default)
        => Task.FromResult(new PreferenciaNotificacion { EmpresaId = empresaId });

    public Task<int> SendToEmpresaAsync(
        int empresaId, string titulo, string mensaje, string screen,
        bool soloAdministradores = true, CancellationToken ct = default)
        => Task.FromResult(0);

    public Task<int> SendBroadcastAsync(
        string titulo, string mensaje, bool soloActivas, CancellationToken ct = default)
        => Task.FromResult(0);
}

/// <summary>
/// La nómina es el otro sitio donde la app mueve dinero, y tampoco tenía tests.
///
/// Lo que se verifica es la invariante contable: MontoPagado nunca puede pasar de
/// MontoTotal, y el estado (Pendiente / Parcial / Pagado) tiene que reflejar
/// exactamente lo abonado. Un céntimo de descuadre aquí deja una nómina
/// eternamente en "Parcial" o le paga de más a un empleado.
/// </summary>
public class NominaTests
{
    private static async Task<(PagosController api, AppDbContext db, PagoNomina nomina)>
        Preparar(string nombreBd, decimal montoTotal = 100m)
    {
        var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 }, nombreBd);

        // El módulo de pagos se valida contra la config de la empresa.
        // Telefono y Direccion son no anulables en el modelo (los avisos CS8618
        // del build): sin ellos InMemory rechaza el insert.
        db.Empresas.Add(new Empresa
        {
            Id = 1,
            Nombre = "Negocio",
            Telefono = "0412-0000000",
            Direccion = "Calle 1",
            Estado = EstadoSuscripcion.Activo,
        });
        db.EmpresaConfigs.Add(new EmpresaConfig
        {
            EmpresaId = 1,
            Plan = PlanEmpresa.Premium,
            ModuloPagosEmpleados = true,
        });

        var empleado = new Empleado
        {
            EmpresaId = 1, Nombre = "Barbero", Activo = true, PorcentajeComision = 50,
        };
        db.Empleados.Add(empleado);
        await db.SaveChangesAsync();

        var nomina = new PagoNomina
        {
            EmpresaId = 1,
            EmpleadoId = empleado.Id,
            Empleado = empleado,
            MontoTotal = montoTotal,
            MontoPagado = 0,
            Estado = EstadoPago.Pendiente,
            FechaCorte = DateTime.UtcNow,
        };
        db.PagosNomina.Add(nomina);
        await db.SaveChangesAsync();

        var api = new PagosController(
            db,
            new UserContextFalso { EmpresaId = 1 },
            new PlanValidationService(db, Ayudas.AnunciosPorDefecto()),
            new PushFalso(),
            NullLogger<PagosController>.Instance);

        return (api, db, nomina);
    }

    [Fact]
    public async Task Un_abono_parcial_deja_la_nomina_en_Parcial()
    {
        var (api, db, nomina) = await Preparar(nameof(Un_abono_parcial_deja_la_nomina_en_Parcial));

        await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 40m });

        var actual = await db.PagosNomina.SingleAsync();
        Assert.Equal(40m, actual.MontoPagado);
        Assert.Equal(EstadoPago.Parcial, actual.Estado);
    }

    [Fact]
    public async Task Abonar_el_total_deja_la_nomina_Pagada()
    {
        var (api, db, nomina) = await Preparar(nameof(Abonar_el_total_deja_la_nomina_Pagada));

        await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 100m });

        var actual = await db.PagosNomina.SingleAsync();
        Assert.Equal(100m, actual.MontoPagado);
        Assert.Equal(EstadoPago.Pagado, actual.Estado);
    }

    [Fact]
    public async Task Varios_abonos_suman_hasta_completar()
    {
        var (api, db, nomina) = await Preparar(nameof(Varios_abonos_suman_hasta_completar));

        await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 30m });
        await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 70m });

        var actual = await db.PagosNomina.SingleAsync();
        Assert.Equal(100m, actual.MontoPagado);
        Assert.Equal(EstadoPago.Pagado, actual.Estado);
        Assert.Equal(2, await db.AbonosPago.CountAsync());
    }

    [Fact]
    public async Task No_se_puede_abonar_mas_de_lo_pendiente()
    {
        // Sin esta guarda, MontoPagado supera a MontoTotal y el reporte de gasto
        // en nómina queda falseado para siempre.
        var (api, db, nomina) = await Preparar(nameof(No_se_puede_abonar_mas_de_lo_pendiente));

        await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 60m });
        var exceso = await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 50m });

        Assert.IsType<BadRequestObjectResult>(exceso);
        var actual = await db.PagosNomina.SingleAsync();
        Assert.Equal(60m, actual.MontoPagado);
        Assert.True(actual.MontoPagado <= actual.MontoTotal);
    }

    [Fact]
    public async Task Una_nomina_ya_pagada_no_admite_mas_abonos()
    {
        var (api, db, nomina) = await Preparar(nameof(Una_nomina_ya_pagada_no_admite_mas_abonos));

        await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 100m });
        var extra = await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 10m });

        Assert.IsType<BadRequestObjectResult>(extra);
        Assert.Equal(100m, (await db.PagosNomina.SingleAsync()).MontoPagado);
        Assert.Equal(1, await db.AbonosPago.CountAsync());
    }

    [Fact]
    public async Task Un_abono_de_cero_o_negativo_se_rechaza()
    {
        var (api, db, nomina) = await Preparar(nameof(Un_abono_de_cero_o_negativo_se_rechaza));

        Assert.IsType<BadRequestObjectResult>(
            await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = 0m }));
        Assert.IsType<BadRequestObjectResult>(
            await api.AbonarNomina(new CrearAbonoNominaDto { PagoNominaId = nomina.Id, Monto = -25m }));

        Assert.Equal(0, await db.AbonosPago.CountAsync());
    }
}
