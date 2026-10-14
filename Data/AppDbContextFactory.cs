using BarberShop.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BarberShop.Data;

/// <summary>Genera migraciones sin arrancar servicios ni conectar a producción.</summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Para aplicar migraciones se exige una conexión explícita mediante --connection.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=design_only;Username=design_only;Timeout=1")
            .Options;
        return new AppDbContext(options, new DesignUserContext());
    }

    private sealed class DesignUserContext : IUserContext
    {
        public int? EmpresaId => null;
        public string UsuarioId => "";
        public bool IsSuperAdmin => false;
    }
}
