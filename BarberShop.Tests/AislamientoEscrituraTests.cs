using BarberShop.Controllers;
using BarberShop.Data;
using BarberShop.Dtos;
using BarberShop.Enum;
using BarberShop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

/// <summary>
/// AislamientoTenantTests cubre la LECTURA. Esto cubre la ESCRITURA, que estaba
/// sin proteger: los ids del cuerpo se copiaban a la entidad sin comprobar de
/// quien eran.
///
/// La clave foranea apunta a la tabla entera, no al tenant, asi que el INSERT
/// pasaba sin error y quedaba una cita de la empresa A apuntando a un cliente de
/// la B. MapToCitaResponse devolvia despues su nombre y su telefono: una fuga de
/// datos que se explota probando numeros.
/// </summary>
public class AislamientoEscrituraTests
{
    /// <summary>Dos empresas en la misma base, cada una con sus datos.</summary>
    private static async Task<(AppDbContext db, int clienteAjeno, int empleadoAjeno)>
        PrepararDosEmpresas(string nombreBd)
    {
        // Se puebla como SuperAdmin para poder crear datos de las dos empresas.
        var siembra = Ayudas.CrearContexto(
            new UserContextFalso { EmpresaId = 1, IsSuperAdmin = true }, nombreBd);

        var clienteAjeno = new Cliente { EmpresaId = 2, Nombre = "Cliente de la otra empresa" };
        var empleadoAjeno = new Empleado { EmpresaId = 2, Nombre = "Empleado ajeno", Activo = true };
        siembra.AddRange(clienteAjeno, empleadoAjeno);

        siembra.AddRange(
            new Cliente { EmpresaId = 1, Nombre = "Cliente propio" },
            new Empleado { EmpresaId = 1, Nombre = "Empleado propio", Activo = true },
            new Servicio
            {
                EmpresaId = 1, Nombre = "Corte", Categoria = "Cabello",
                Precio = 10, DuracionMinutos = 30, Activo = true,
            });
        await siembra.SaveChangesAsync();

        // El contexto de la peticion: empresa 1, sin privilegios.
        var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 }, nombreBd);
        return (db, clienteAjeno.Id, empleadoAjeno.Id);
    }

    private static async Task<CreateCitaBaseDto> DtoValido(AppDbContext db) => new()
    {
        ClienteId = await db.Clientes.Select(c => c.Id).FirstAsync(),
        EmpleadoId = await db.Empleados.Select(e => e.Id).FirstAsync(),
        Fecha = DateTime.UtcNow.AddHours(3),
        Servicios =
        [
            new ServicioCantidadDto
            {
                ServicioId = await db.Servicios.Select(s => s.Id).FirstAsync(),
                Cantidad = 1,
            }
        ],
    };

    [Fact]
    public async Task No_se_puede_agendar_para_un_cliente_de_otra_empresa()
    {
        var (db, clienteAjeno, _) = await PrepararDosEmpresas(
            nameof(No_se_puede_agendar_para_un_cliente_de_otra_empresa));

        var dto = await DtoValido(db);
        dto.ClienteId = clienteAjeno;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CitasController(db).AgendarCita(dto));

        Assert.Contains("Cliente inválido", ex.Message);
        Assert.Equal(0, await db.Citas.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task No_se_puede_agendar_con_un_empleado_de_otra_empresa()
    {
        var (db, _, empleadoAjeno) = await PrepararDosEmpresas(
            nameof(No_se_puede_agendar_con_un_empleado_de_otra_empresa));

        var dto = await DtoValido(db);
        dto.EmpleadoId = empleadoAjeno;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CitasController(db).AgendarCita(dto));

        Assert.Contains("Empleado inválido", ex.Message);
        Assert.Equal(0, await db.Citas.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task No_se_puede_agendar_con_ids_inexistentes()
    {
        var (db, _, _) = await PrepararDosEmpresas(
            nameof(No_se_puede_agendar_con_ids_inexistentes));

        var dto = await DtoValido(db);
        dto.ClienteId = 999999;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CitasController(db).AgendarCita(dto));
    }

    [Fact]
    public async Task Con_datos_propios_la_cita_si_se_crea()
    {
        // Contrapeso: la validacion no puede haber roto el camino bueno.
        var (db, _, _) = await PrepararDosEmpresas(
            nameof(Con_datos_propios_la_cita_si_se_crea));

        await new CitasController(db).AgendarCita(await DtoValido(db));

        var cita = await db.Citas.SingleAsync();
        Assert.Equal(1, cita.EmpresaId);
        Assert.Equal(EstadoCita.Pendiente, cita.Estado);
    }

    [Fact]
    public async Task No_se_puede_abonar_a_un_cliente_de_otra_empresa()
    {
        var (db, clienteAjeno, _) = await PrepararDosEmpresas(
            nameof(No_se_puede_abonar_a_un_cliente_de_otra_empresa));

        var resultado = await new CuentaClienteController(db).CrearAbono(new CrearAbonoDto
        {
            ClienteId = clienteAjeno,
            Monto = 50,
        });

        Assert.IsType<BadRequestObjectResult>(resultado);
        Assert.Equal(0, await db.CuentasCliente.IgnoreQueryFilters().CountAsync());
    }
}
