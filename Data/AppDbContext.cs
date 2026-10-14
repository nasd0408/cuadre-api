using BarberShop.Interfaces;
using BarberShop.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace BarberShop.Data
{
    /// <summary>
    /// El DbContext es la "puerta" de Entity Framework Core hacia PostgreSQL.
    /// Cada DbSet&lt;T&gt; representa una tabla; al hacer LINQ sobre un DbSet, EF lo
    /// traduce a SQL. Hereda de IdentityDbContext, que ya trae las tablas de
    /// ASP.NET Identity (AspNetUsers, AspNetRoles, AspNetUserRoles, ...).
    ///
    /// Aqui vive la pieza clave del multi-tenant: un filtro global que hace que
    /// cada consulta solo vea las filas de la empresa del usuario logueado.
    /// </summary>
    public class AppDbContext : IdentityDbContext<Usuario>
    {
        private readonly IUserContext _userContext;

        /// <summary>
        /// Constructor usado por la inyeccion de dependencias. Recibe las opciones
        /// (cadena de conexion, proveedor Npgsql, configuradas en Program.cs) y el
        /// IUserContext, que dice que empresa y usuario hacen la peticion actual.
        /// </summary>
        public AppDbContext(DbContextOptions<AppDbContext> options, IUserContext userContext)
            : base(options)
        {
            _userContext = userContext;
        }

        // Existing DbSets
        public DbSet<Empresa> Empresas { get; set; }
        public DbSet<PerfilPublicoEmpresa> PerfilesPublicos { get; set; }
        public DbSet<ReservaPublica> ReservasPublicas { get; set; }
        public DbSet<Sucursal> Sucursales { get; set; }
        public DbSet<Empleado> Empleados { get; set; }
        public DbSet<TramoHorario> TramosHorario { get; set; }
        public DbSet<AusenciaEmpleado> AusenciasEmpleados { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Cita> Citas { get; set; }
        public DbSet<PagoNomina> PagosNomina { get; set; }
        public DbSet<AbonoPago> AbonosPago { get; set; }
        public DbSet<Servicio> Servicios { get; set; }
        public DbSet<CitaServicio> CitasServicios { get; set; }
        public DbSet<CuentaCliente> CuentasCliente { get; set; }
        public DbSet<HistorialSuscripcion> HistorialSuscripciones { get; set; }
        public DbSet<Recordatorio> Recordatorios { get; set; }
        public DbSet<CodigoVerificacion> CodigosVerificacion { get; set; }
        public DbSet<EmpresaConfig> EmpresaConfigs { get; set; }
        public DbSet<PagoSuscripcion> PagoSuscripcion { get; set; } = default!;
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<ConfiguracionMensajeria> ConfiguracionesMensajeria { get; set; }
        public DbSet<DispositivoPush> DispositivosPush { get; set; }
        public DbSet<PreferenciaNotificacion> PreferenciasNotificacion { get; set; }


        // Estas dos propiedades se leen DENTRO del filtro global (ver
        // OnModelCreating). EF las evalua en cada consulta, no una sola vez, por
        // eso el filtro cambia segun quien este logueado.
        public int? CurrentEmpresaId => _userContext.EmpresaId;
        public bool IsSuperAdmin => _userContext.IsSuperAdmin;

        /// <summary>
        /// Convenciones que aplican a TODO el modelo antes de configurar cada
        /// entidad. Aqui se fuerza el tipo de columna de todas las fechas.
        /// </summary>
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // La BD de producción usa "timestamp with time zone" en todas las columnas
            // DateTime (schema original). El switch Npgsql.EnableLegacyTimestampBehavior
            // hace que el provider mapee DateTime a "timestamp without time zone" al
            // generar migraciones, lo que produciría AlterColumn masivos falsos.
            // Fijamos el tipo explícitamente para que el modelo coincida con la BD real.
            configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp with time zone");
            configurationBuilder.Properties<DateTime?>().HaveColumnType("timestamp with time zone");
        }

        /// <summary>
        /// Configuracion del modelo con la "Fluent API" de EF: indices, relaciones,
        /// borrado en cascada, tokens de concurrencia y filtros globales. EF lo
        /// ejecuta UNA vez al arrancar y cachea el resultado; tambien es lo que
        /// lee "dotnet ef migrations add" para generar las migraciones.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<TramoHorario>().Property(t => t.EmpresaId).IsRequired();
            builder.Entity<TramoHorario>().HasOne<Empleado>().WithMany()
                .HasForeignKey(t => t.EmpleadoId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<TramoHorario>().HasIndex(t => new { t.EmpresaId, t.EmpleadoId, t.DiaSemana });
            builder.Entity<AusenciaEmpleado>().Property(a => a.EmpresaId).IsRequired();
            builder.Entity<AusenciaEmpleado>().HasOne<Empleado>().WithMany()
                .HasForeignKey(a => a.EmpleadoId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<AusenciaEmpleado>().HasIndex(a => new { a.EmpresaId, a.EmpleadoId, a.InicioUtc, a.FinUtc });

            builder.Entity<PerfilPublicoEmpresa>().Property(p => p.EmpresaId).IsRequired();
            builder.Entity<PerfilPublicoEmpresa>().HasIndex(p => p.EmpresaId).IsUnique();
            builder.Entity<PerfilPublicoEmpresa>().HasIndex(p => p.TokenReservas).IsUnique();
            builder.Entity<PerfilPublicoEmpresa>().HasOne<Sucursal>().WithMany()
                .HasForeignKey(p => p.SucursalReservasId).OnDelete(DeleteBehavior.Restrict);
            builder.Entity<ReservaPublica>().Property(r => r.EmpresaId).IsRequired();
            builder.Entity<ReservaPublica>().HasIndex(r => r.TokenHash).IsUnique();
            builder.Entity<ReservaPublica>().HasIndex(r => r.CitaId).IsUnique();
            builder.Entity<ReservaPublica>().HasOne(r => r.Cita).WithOne()
                .HasForeignKey<ReservaPublica>(r => r.CitaId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<PerfilPublicoEmpresa>().HasOne<Empresa>().WithOne()
                .HasForeignKey<PerfilPublicoEmpresa>(p => p.EmpresaId).OnDelete(DeleteBehavior.Cascade);
            builder.Entity<Sucursal>().Property(s => s.EmpresaId).IsRequired();
            builder.Entity<Sucursal>().HasOne<Empresa>().WithMany()
                .HasForeignKey(s => s.EmpresaId).OnDelete(DeleteBehavior.Cascade);

            // Configure CodigoVerificacion (no tenant filter needed)
            builder.Entity<CodigoVerificacion>(entity =>
            {
                entity.HasIndex(c => new { c.UserId, c.Tipo, c.Usado });
                entity.HasIndex(c => c.FechaExpiracion);

                entity.HasOne(c => c.Usuario)
                    .WithMany()
                    .HasForeignKey(c => c.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            // Configure RefreshToken (no tenant filter needed)
            builder.Entity<RefreshToken>(entity =>
            {
                entity.HasIndex(r => r.TokenHash).IsUnique();
                entity.HasIndex(r => r.UserId);

                entity.HasOne(r => r.Usuario)
                    .WithMany()
                    .HasForeignKey(r => r.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure EmpresaConfig (1:1 con Empresa, sin tenant filter)
            builder.Entity<EmpresaConfig>(entity =>
            {
                entity.HasIndex(e => e.EmpresaId).IsUnique();

                entity.HasOne(e => e.Empresa)
                      .WithOne(e => e.Config)
                      .HasForeignKey<EmpresaConfig>(e => e.EmpresaId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ConfiguracionMensajeria: 1:1 con Empresa (además del tenant filter de BaseEntity)
            builder.Entity<ConfiguracionMensajeria>()
                .HasIndex(c => c.EmpresaId).IsUnique();

            // PreferenciaNotificacion: 1:1 con Empresa
            builder.Entity<PreferenciaNotificacion>()
                .HasIndex(p => p.EmpresaId).IsUnique();

            // DispositivoPush: sin tenant filter (el job lee todas las empresas)
            builder.Entity<DispositivoPush>(entity =>
            {
                entity.HasIndex(d => d.Token).IsUnique();
                entity.HasIndex(d => d.EmpresaId);
                entity.HasOne(d => d.Usuario)
                    .WithMany()
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<Cita>()
                .HasIndex(c => new { c.EmpresaId, c.OfflineId })
                .IsUnique()
                .HasFilter("\"OfflineId\" IS NOT NULL");

            // ── Indices sobre EmpresaId ──
            //
            // El filtro global de tenant inyecta "WHERE EmpresaId = @p" en
            // LITERALMENTE todas las consultas, y 9 de 12 tablas de dominio no
            // tenian ni un indice que la incluyera. El unico de Citas es
            // UNIQUE (EmpresaId, OfflineId) WHERE OfflineId IS NOT NULL: parcial,
            // asi que no sirve para buscar por empresa.
            //
            // Hoy no duele -las tablas tienen cientos de filas, Postgres hace seq
            // scan y va rapido-, pero el coste crece linealmente con el uso y
            // anadirlos ahora es gratis.
            builder.Entity<Cliente>().HasIndex(e => e.EmpresaId);
            builder.Entity<Servicio>().HasIndex(e => e.EmpresaId);
            builder.Entity<CuentaCliente>().HasIndex(e => e.EmpresaId);
            builder.Entity<CitaServicio>().HasIndex(e => e.EmpresaId);
            builder.Entity<PagoNomina>().HasIndex(e => e.EmpresaId);
            builder.Entity<AbonoPago>().HasIndex(e => e.EmpresaId);
            builder.Entity<PagoSuscripcion>().HasIndex(e => e.EmpresaId);

            // Compuestos, siguiendo lo que filtran las consultas reales.
            // Citas se ordena y filtra por fecha en la pantalla principal.
            builder.Entity<Cita>().HasIndex(c => new { c.EmpresaId, c.Fecha });
            builder.Entity<Cita>().HasIndex(c => new { c.EmpresaId, c.Estado });

            // Recordatorios: NO se pone unico a proposito.
            //
            // Seria el arreglo natural para la carrera de RecordatorioController,
            // que hace INSERT desde un GET (SincronizarAsync) comprobando en
            // memoria y no en BD: dos peticiones simultaneas pasan ambas.
            //
            // Pero produccion YA tiene duplicados -4 de 7 filas el 21/08/2026-,
            // asi que crear el indice unico haria fallar la migracion. Limpiar
            // datos es una decision del dueno, no algo que meter de tapadillo en
            // un indice. Queda como indice normal, que igual acelera la consulta.
            builder.Entity<Recordatorio>()
                .HasIndex(r => new { r.EmpresaId, r.ClienteId });

            // El job horario de suscripciones filtra por estado.
            builder.Entity<Empresa>().HasIndex(e => e.Estado);

            // ── Concurrencia optimista sobre las entidades de dinero ──
            //
            // Sin esto, dos peticiones simultaneas leen el mismo estado, ambas
            // pasan la guarda ("if estado != PagoPendiente") y ambas insertan un
            // movimiento. El UPDATE del estado es idempotente, asi que NADA falla
            // y el cliente acaba con dos abonos por el mismo servicio. Pasa con un
            // doble toque en el movil, que no es un caso raro.
            //
            // Se usa xmin, la columna de sistema de PostgreSQL: no anade columna
            // ni ocupa espacio, y la migracion no lleva DDL. EF incluye
            // "AND xmin = @original" en el UPDATE, asi que el segundo en llegar
            // afecta 0 filas y lanza DbUpdateConcurrencyException.
            //
            // Como el INSERT del movimiento y el UPDATE del estado viajan en el
            // mismo SaveChanges (que EF envuelve en transaccion), al fallar el
            // UPDATE se revierte tambien el movimiento. Ese es el arreglo.
            //
            // ManejadorGlobalDeExcepciones lo traduce a 409.
            // Npgsql 10 elimino el atajo UseXminAsConcurrencyToken(), asi que se
            // mapea a mano contra la columna de sistema, que es lo que hacia por
            // dentro. No genera DDL: xmin ya existe en toda tabla de PostgreSQL.
            foreach (var tipo in new[] {
                typeof(Cita), typeof(CuentaCliente),
                typeof(PagoNomina), typeof(PagoSuscripcion) })
            {
                builder.Entity(tipo)
                    .Property<uint>("xmin")
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            }

            // ── Filtro global de tenant (multi-tenancy) ──
            //
            // Para cada entidad que hereda de BaseEntity se construye, con arboles
            // de expresion (System.Linq.Expressions), el equivalente a escribir:
            //
            //   builder.Entity<Cliente>().HasQueryFilter(e =>
            //       IsSuperAdmin || e.EmpresaId == CurrentEmpresaId);
            //
            // Se hace con Expression porque no se conoce el tipo en tiempo de
            // compilacion (se recorre la lista de entidades). Resultado: cualquier
            // _context.Clientes.ToList() anade solo "WHERE EmpresaId = X" y es
            // imposible olvidarse de filtrar. Para saltarse el filtro a proposito
            // existe .IgnoreQueryFilters().
            //
            // Si la entidad tiene una propiedad bool "Activo" (y no es una de las
            // excluidas, que manejan el borrado logico a mano), tambien se filtra
            // por Activo == true (soft delete).
            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    var dbContext = Expression.Constant(this);

                    var empresaProperty = Expression.Property(parameter, nameof(BaseEntity.EmpresaId));
                    var empresaPropertyAsNullable = Expression.Convert(empresaProperty, typeof(int?));
                    var currentEmpresaId = Expression.Property(dbContext, nameof(CurrentEmpresaId));
                    var isSuperAdmin = Expression.Property(dbContext, nameof(IsSuperAdmin));

                    var empresaEqual = Expression.Equal(empresaPropertyAsNullable, currentEmpresaId);
                    var tenantFilter = Expression.OrElse(isSuperAdmin, empresaEqual);

                    Expression finalFilter = tenantFilter;

                    var activoProp = entityType.ClrType.GetProperty("Activo");
                    if (activoProp != null
                     && activoProp.PropertyType == typeof(bool)
                     && entityType.ClrType != typeof(Servicio)
                     && entityType.ClrType != typeof(Cliente)
                     && entityType.ClrType != typeof(Empleado)
                     && entityType.ClrType != typeof(Cita))
                    {
                        var activoProperty = Expression.Property(parameter, "Activo");
                        var activoComparison = Expression.Equal(activoProperty, Expression.Constant(true));
                        finalFilter = Expression.AndAlso(finalFilter, activoComparison);
                    }

                    var lambda = Expression.Lambda(finalFilter, parameter);
                    builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
                }
            }
        }

        /// <summary>
        /// Se ejecuta en cada SaveChangesAsync (el momento en que EF manda los
        /// INSERT/UPDATE/DELETE pendientes a la BD). Antes de guardar, recorre el
        /// ChangeTracker (la lista de entidades que EF esta vigilando) y a cada
        /// entidad NUEVA le pone automaticamente la EmpresaId del usuario y la
        /// fecha de creacion. Asi los controladores no tienen que acordarse.
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    // Si ya tiene EmpresaId (ej: asignado manual en endpoint público), respetarlo
                    if (entry.Entity.EmpresaId == null || entry.Entity.EmpresaId == 0)
                    {
                        entry.Entity.EmpresaId = _userContext.EmpresaId
                            ?? throw new Exception("No se puede crear sin EmpresaId");
                    }
                    entry.Entity.FechaCreacion = DateTime.UtcNow;
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
