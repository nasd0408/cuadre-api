using BarberShop.Data;
using BarberShop.Services;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Tests;

/// <summary>
/// Estas pruebas no abren una conexión: fuerzan al proveedor real de PostgreSQL
/// a traducir las consultas del reporte. Si EF deja de soportar una forma de
/// GroupBy, ToQueryString falla aquí antes de llegar a producción.
/// </summary>
public class ConsultasClientelaSqlTests
{
    private static AppDbContext CrearContextoPostgres()
    {
        var opciones = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=solo_traduccion;Username=test;Password=test")
            .Options;

        return new AppDbContext(
            opciones,
            new UserContextFalso { EmpresaId = 7 });
    }

    [Fact]
    public void Clientes_en_riesgo_se_traduce_a_having_y_respeta_el_tenant()
    {
        using var db = CrearContextoPostgres();

        var sql = ConsultasClientela
            .ClientesEnRiesgo(db, DateTime.UtcNow.AddDays(-60))
            .ToQueryString();

        Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAX", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EmpresaId", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Primera_visita_se_traduce_a_min_y_respeta_el_tenant()
    {
        using var db = CrearContextoPostgres();

        var sql = ConsultasClientela
            .PrimeraVisitaDe(db, new[] { 10, 20 })
            .ToQueryString();

        Assert.Contains("MIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EmpresaId", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saldos_deudores_agrega_en_SQL_y_no_trae_el_historico()
    {
        using var db = CrearContextoPostgres();

        var sql = ConsultasClientela.SaldosDeudores(db).ToQueryString();

        // Si esto deja de traducirse, EF Core 10 lanza en tiempo de ejecucion y
        // el dashboard -la primera pantalla que ve el usuario- devuelve un 500.
        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CASE", sql, StringComparison.OrdinalIgnoreCase);
        // El filtro sobre el agregado tiene que ser HAVING, no un filtro en C#.
        Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
        // Y sigue respetando el aislamiento por empresa.
        Assert.Contains("EmpresaId", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Saldos_de_una_empresa_agrega_en_SQL_y_acota_al_tenant_a_mano()
    {
        using var db = CrearContextoPostgres();

        // La usa el job de notificaciones, que corre SIN sesion: se salta el
        // filtro global, asi que el WHERE por empresa tiene que estar igualmente.
        var sql = ConsultasClientela.SaldosDeudoresDe(db, 42).ToQueryString();

        Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EmpresaId", sql, StringComparison.OrdinalIgnoreCase);
    }
}
