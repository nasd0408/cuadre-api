using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

/// <summary>
/// El saldo del cliente es la propiedad mas importante del sistema: es el dinero
/// que el negocio cree que le deben. No tenia ni un test.
///
/// Cada flujo de cita mueve la cuenta del cliente con un Cargo (debe) o un Abono
/// (pago). Lo que se verifica aqui no es que el codigo haga lo que hace, sino que
/// LAS CUENTAS CUADREN: cobrada deja saldo 0, a deber deja saldo positivo, y
/// reabrir vuelve a dejar deuda.
/// </summary>
public class ContabilidadCitasTests
{
    private const decimal PrecioCorte = 25m;

    private static async Task<(CitasController api, AppDbContext db, int clienteId, int servicioId, int empleadoId)>
        Preparar(string nombreBd)
    {
        var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 }, nombreBd);

        var cliente = new Cliente { EmpresaId = 1, Nombre = "Cliente" };
        var empleado = new Empleado { EmpresaId = 1, Nombre = "Barbero", Activo = true };
        var servicio = new Servicio
        {
            EmpresaId = 1, Nombre = "Corte", Categoria = "Cabello",
            Precio = PrecioCorte, DuracionMinutos = 30, Activo = true,
        };
        db.AddRange(cliente, empleado, servicio);
        await db.SaveChangesAsync();

        return (new CitasController(db), db, cliente.Id, servicio.Id, empleado.Id);
    }

    /// <summary>Lo que el cliente debe: cargos menos abonos.</summary>
    private static async Task<decimal> SaldoAsync(AppDbContext db, int clienteId)
    {
        var movimientos = await db.CuentasCliente
            .Where(m => m.ClienteId == clienteId)
            .ToListAsync();

        return movimientos.Sum(m => m.Tipo == TipoMovimiento.Cargo ? m.Monto : -m.Monto);
    }

    private static CreateCitaBaseDto Dto(int clienteId, int empleadoId, int servicioId, int cantidad = 1) => new()
    {
        ClienteId = clienteId,
        EmpleadoId = empleadoId,
        Fecha = DateTime.UtcNow.AddHours(2),
        Servicios = [new ServicioCantidadDto { ServicioId = servicioId, Cantidad = cantidad }],
    };

    [Fact]
    public async Task Cita_cobrada_al_momento_deja_al_cliente_sin_deuda()
    {
        var (api, db, cliente, servicio, empleado) = await Preparar(
            nameof(Cita_cobrada_al_momento_deja_al_cliente_sin_deuda));

        await api.CreatePagada(new CreateCitaPagadaDto
        {
            ClienteId = cliente,
            EmpleadoId = empleado,
            Fecha = DateTime.UtcNow,
            Servicios = [new ServicioCantidadDto { ServicioId = servicio, Cantidad = 1 }],
        });

        // Un Cargo y un Abono por el mismo importe: el cliente no debe nada.
        Assert.Equal(0m, await SaldoAsync(db, cliente));
        Assert.Equal(2, await db.CuentasCliente.CountAsync());
        Assert.Equal(EstadoCita.Cerrada, (await db.Citas.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Cita_a_deber_deja_al_cliente_debiendo_el_total()
    {
        var (api, db, cliente, servicio, empleado) = await Preparar(
            nameof(Cita_a_deber_deja_al_cliente_debiendo_el_total));

        await api.CreatePendiente(Dto(cliente, empleado, servicio, cantidad: 2));

        // Solo Cargo, sin Abono.
        Assert.Equal(PrecioCorte * 2, await SaldoAsync(db, cliente));
        Assert.Equal(EstadoCita.PagoPendiente, (await db.Citas.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Cobrar_una_cita_a_deber_salda_la_cuenta()
    {
        var (api, db, cliente, servicio, empleado) = await Preparar(
            nameof(Cobrar_una_cita_a_deber_salda_la_cuenta));

        await api.CreatePendiente(Dto(cliente, empleado, servicio));
        var citaId = (await db.Citas.SingleAsync()).Id;

        await api.Cobrar(citaId, null);

        Assert.Equal(0m, await SaldoAsync(db, cliente));
        Assert.Equal(EstadoCita.Cerrada, (await db.Citas.SingleAsync()).Estado);
    }

    [Fact]
    public async Task Cobrar_dos_veces_no_genera_dos_abonos()
    {
        // La guarda de estado es lo unico que protege en secuencia; la proteccion
        // ante peticiones SIMULTANEAS es el token de concurrencia, que EF InMemory
        // no puede simular. Esto cubre al menos el doble toque en serie.
        var (api, db, cliente, servicio, empleado) = await Preparar(
            nameof(Cobrar_dos_veces_no_genera_dos_abonos));

        await api.CreatePendiente(Dto(cliente, empleado, servicio));
        var citaId = (await db.Citas.SingleAsync()).Id;

        await api.Cobrar(citaId, null);
        var segundo = await api.Cobrar(citaId, null);

        Assert.IsType<BadRequestObjectResult>(segundo);
        Assert.Equal(0m, await SaldoAsync(db, cliente));
        Assert.Equal(
            1,
            await db.CuentasCliente.CountAsync(m => m.Tipo == TipoMovimiento.Abono));
    }

    [Fact]
    public async Task Reabrir_una_cita_cerrada_vuelve_a_dejar_la_deuda()
    {
        var (api, db, cliente, servicio, empleado) = await Preparar(
            nameof(Reabrir_una_cita_cerrada_vuelve_a_dejar_la_deuda));

        await api.CreatePendiente(Dto(cliente, empleado, servicio));
        var citaId = (await db.Citas.SingleAsync()).Id;
        await api.Cobrar(citaId, null);

        await api.ReabrirCita(citaId, new ReabrirCitaDto { Motivo = "El cliente reclamó" });

        // Vuelve a deber: el abono anterior sigue, pero hay un cargo nuevo.
        Assert.Equal(PrecioCorte, await SaldoAsync(db, cliente));
    }

    [Fact]
    public async Task El_precio_se_toma_del_catalogo_y_no_del_cuerpo_de_la_peticion()
    {
        var (api, db, cliente, servicio, empleado) = await Preparar(
            nameof(El_precio_se_toma_del_catalogo_y_no_del_cuerpo_de_la_peticion));

        await api.CreatePendiente(Dto(cliente, empleado, servicio));

        var linea = await db.CitasServicios.SingleAsync();
        Assert.Equal(PrecioCorte, linea.Precio);
        Assert.Equal(PrecioCorte, await SaldoAsync(db, cliente));
    }

    [Fact]
    public async Task Una_cita_sin_servicios_no_se_crea()
    {
        var (api, db, cliente, _, empleado) = await Preparar(
            nameof(Una_cita_sin_servicios_no_se_crea));

        var dto = new CreateCitaBaseDto
        {
            ClienteId = cliente,
            EmpleadoId = empleado,
            Fecha = DateTime.UtcNow.AddHours(2),
            Servicios = [],
        };

        // Lo traduce a 400 el manejador global; antes salia como 500.
        await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreatePendiente(dto));
        Assert.Equal(0, await db.Citas.CountAsync());
        Assert.Equal(0, await db.CuentasCliente.CountAsync());
    }
}
