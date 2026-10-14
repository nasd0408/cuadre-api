using BarberShop.Data;
using BarberShop.Helpers;
using BarberShop.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarberShop.Tests;

/// <summary>Contexto de usuario falso para simular a quién pertenece la sesión.</summary>
public class UserContextFalso : IUserContext
{
    public int? EmpresaId { get; set; }
    public string UsuarioId { get; set; } = "test-user";
    public bool IsSuperAdmin { get; set; }
}

/// <summary>IOptionsMonitor mínimo: devuelve siempre el mismo valor.</summary>
public class OptionsMonitorFalso<T> : IOptionsMonitor<T>
{
    private readonly T _valor;

    public OptionsMonitorFalso(T valor) => _valor = valor;

    public T CurrentValue => _valor;
    public T Get(string? name) => _valor;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public static class Ayudas
{
    /// <summary>
    /// Crea un AppDbContext sobre EF InMemory.
    ///
    /// Se usa InMemory y no Postgres a propósito: lo que se quiere verificar aquí es
    /// el filtro global de multi-tenancy y las reglas de plan, que viven en el modelo
    /// de EF, no en el motor. Así los tests corren sin infraestructura.
    /// </summary>
    public static AppDbContext CrearContexto(IUserContext userContext, string? nombreBd = null)
    {
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(nombreBd ?? Guid.NewGuid().ToString())
            // El filtro de tenant es justamente lo que se está probando: la advertencia
            // de InMemory sobre transacciones no aplica y ensucia la salida.
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(opciones, userContext);
    }

    public static OptionsMonitorFalso<AnunciosOptions> AnunciosPorDefecto() =>
        new(new AnunciosOptions());
}
