using System.Data;
using BarberShop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BarberShop.Services;

/// <summary>
/// Serializa cambios de agenda de una empresa entre procesos/instancias de API.
/// La consulta de disponibilidad debe ocurrir DESPUÉS de adquirir el bloqueo.
/// ReadCommitted permite ver los cambios del anterior titular cuando termina.
/// El bloqueo transaccional se libera en commit, rollback o pérdida de conexión.
/// </summary>
public static class TransaccionAgenda
{
    public const int EspacioBloqueo = 1129661508; // Espacio reservado para agenda de CuadreAPP.

    public static async Task<IDbContextTransaction> Abrir(AppDbContext db, CancellationToken ct = default)
    {
        var empresa = db.CurrentEmpresaId
            ?? throw new InvalidOperationException("Selecciona una empresa para modificar su agenda.");
        // InMemory se usa exclusivamente en pruebas de reglas; no simula bloqueos.
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            return await db.Database.BeginTransactionAsync(ct);
        if (!db.Database.IsNpgsql())
            throw new NotSupportedException("La protección de agenda requiere PostgreSQL.");
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Evita esperar indefinidamente. SET LOCAL no contamina conexiones del pool.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({EspacioBloqueo}, {empresa})", ct);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
