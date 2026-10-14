using BarberShop.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

/// <summary>
/// Aislamiento entre empresas. Es la garantía más importante de todo el sistema:
/// una fuga aquí significa que un negocio ve los clientes de otro.
///
/// El filtro vive en un query filter global de AppDbContext, aplicado por convención
/// a todo lo que herede de BaseEntity. Como es implícito, es fácil romperlo sin
/// notarlo, y por eso conviene tenerlo cubierto.
/// </summary>
public class AislamientoTenantTests
{
    private const string BD = "aislamiento";

    /// <summary>Siembra dos empresas con un cliente cada una, en la misma base.</summary>
    private static void Sembrar(string nombreBd)
    {
        var comoEmpresa1 = new UserContextFalso { EmpresaId = 1 };
        using (var db = Ayudas.CrearContexto(comoEmpresa1, nombreBd))
        {
            db.Clientes.Add(new Cliente { Nombre = "Cliente de la empresa 1" });
            db.SaveChangesAsync().GetAwaiter().GetResult();
        }

        var comoEmpresa2 = new UserContextFalso { EmpresaId = 2 };
        using (var db = Ayudas.CrearContexto(comoEmpresa2, nombreBd))
        {
            db.Clientes.Add(new Cliente { Nombre = "Cliente de la empresa 2" });
            db.SaveChangesAsync().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public async Task Una_empresa_solo_ve_sus_propios_clientes()
    {
        var bd = $"{BD}-propios";
        Sembrar(bd);

        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 }, bd);
        var clientes = await db.Clientes.ToListAsync();

        Assert.Single(clientes);
        Assert.Equal("Cliente de la empresa 1", clientes[0].Nombre);
    }

    [Fact]
    public async Task Una_empresa_no_puede_leer_por_id_un_registro_de_otra()
    {
        var bd = $"{BD}-por-id";
        Sembrar(bd);

        int idAjeno;
        using (var comoDos = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 2 }, bd))
        {
            idAjeno = (await comoDos.Clientes.SingleAsync()).Id;
        }

        // Buscar por id explícito tampoco debe saltarse el filtro: es el intento
        // clásico de acceder a datos ajenos adivinando identificadores.
        using var comoUno = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 1 }, bd);
        var ajeno = await comoUno.Clientes.FirstOrDefaultAsync(c => c.Id == idAjeno);

        Assert.Null(ajeno);
    }

    [Fact]
    public async Task El_SuperAdmin_ve_todas_las_empresas()
    {
        var bd = $"{BD}-superadmin";
        Sembrar(bd);

        using var db = Ayudas.CrearContexto(
            new UserContextFalso { EmpresaId = null, IsSuperAdmin = true }, bd);

        var clientes = await db.Clientes.ToListAsync();

        Assert.Equal(2, clientes.Count);
    }

    [Fact]
    public async Task Sin_sesion_no_se_ve_nada()
    {
        var bd = $"{BD}-anonimo";
        Sembrar(bd);

        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = null }, bd);
        var clientes = await db.Clientes.ToListAsync();

        Assert.Empty(clientes);
    }

    [Fact]
    public async Task Al_guardar_se_estampa_la_empresa_de_la_sesion()
    {
        var bd = $"{BD}-estampado";

        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = 7 }, bd);
        db.Clientes.Add(new Cliente { Nombre = "Sin empresa explícita" });
        await db.SaveChangesAsync();

        var guardado = await db.Clientes.SingleAsync();
        Assert.Equal(7, guardado.EmpresaId);
    }

    /// <summary>
    /// Crear sin sesión debe fallar en vez de guardar una fila huérfana: una fila con
    /// EmpresaId nulo no la ve nadie y aparece como pérdida de datos.
    /// </summary>
    [Fact]
    public async Task Guardar_sin_empresa_lanza_excepcion()
    {
        using var db = Ayudas.CrearContexto(new UserContextFalso { EmpresaId = null });
        db.Clientes.Add(new Cliente { Nombre = "Huérfano" });

        await Assert.ThrowsAsync<Exception>(() => db.SaveChangesAsync());
    }
}
