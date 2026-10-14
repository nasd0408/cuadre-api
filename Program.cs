using BarberShop.Data;
using BarberShop.Helpers;
using BarberShop.Hubs;
using BarberShop.Interfaces;
using BarberShop.Middleware;
using Microsoft.Extensions.Options;
using BarberShop.Models;
using BarberShop.Services;
using BarberShop.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

// Program.cs es el punto de entrada de la API. Usa "top-level statements": no hay
// clase Program ni metodo Main explicitos, el compilador los genera.
//
// Se divide en dos fases:
//   1. builder.Services.* -> REGISTRO de servicios en el contenedor de inyeccion
//      de dependencias (DI). Aqui solo se describe "que existe y como se crea".
//   2. app.Use* / app.Map* -> el PIPELINE HTTP: la cadena de middleware por la
//      que pasa cada peticion, en el orden exacto en que se declaran.
var builder = WebApplication.CreateBuilder(args);

// =======================
// Database
// =======================
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql =>
        {
            // Una consulta no puede retener una conexion del pool indefinidamente.
            npgsql.CommandTimeout(30);

            // PENDIENTE: EnableRetryOnFailure. NO activar sin hacer antes el resto.
            //
            // Activarlo hace que EF EXIJA una estrategia de ejecucion alrededor de
            // toda transaccion manual; si no, lanza en tiempo de ejecucion:
            //   "The configured execution strategy 'NpgsqlRetryingExecutionStrategy'
            //    does not support user-initiated transactions."
            //
            // Hay 7 transacciones manuales sin estrategia (AjustesController 237 y
            // 431, AuthController 81, 288 y 641, PagoSuscripcionsController 136,
            // SyncController 67). Solo PagoNominaController:214 la tiene, lo que
            // significa que hoy esa llamada es un no-op: devuelve una estrategia
            // que no reintenta nada.
            //
            // El refactor es mecanico, pero toca registro, login, pagos y
            // sincronizacion, y NO se puede verificar con los tests actuales: EF
            // InMemory ignora BeginTransaction, asi que pasarian en verde estando
            // rotos en produccion. Va cuando existan los tests de integracion con
            // Postgres real (Testcontainers).
        });
#if DEBUG
    options.EnableDetailedErrors();
    options.EnableSensitiveDataLogging();
    options.LogTo(Console.WriteLine, LogLevel.Information);
#endif
});
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
// =======================
// Identity
// =======================
builder.Services
    .AddIdentityCore<Usuario>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;

        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// =======================
// JWT
// =======================
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = AgendaHub.LeerTokenDesdeQuery
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
        ValidAudience = builder.Configuration["JWT:ValidAudience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["JWT:Secret"]!)
        )
    };
});

builder.Services.AddAuthorization();

// =======================
// CORS
// =======================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedApps", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithOrigins(
                "http://localhost:8081",
                "http://localhost:19006",
                "https://localhost:7069",
                "http://127.0.0.1:5500",
                "http://localhost:5500/",
                "https://barbershop-api-dev-gsbrcxdwd2agaab4.canadacentral-01.azurewebsites.net/"
            );
    });
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// =======================
// Forwarded headers (nginx reverse proxy)
// =======================
// Sin esto, detrás de nginx todas las requests parecen venir de 127.0.0.1
// y el rate limiter por IP trataría a todos los usuarios como uno solo.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// =======================
// Rate limiting
// =======================
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Demasiadas solicitudes. Intenta de nuevo en unos minutos.",
            code = "RATE_LIMITED"
        }, cancellationToken);
    };

    static string ClientIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Límite global generoso: protege contra abuso sin afectar uso normal
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1)
        }));

    // Login / registro: frena fuerza bruta de contraseñas
    options.AddPolicy("public-read", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("public-book", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));

    options.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1)
        }));

    // Códigos de verificación y endpoint público de landing:
    // frena fuerza bruta de códigos de 6 dígitos y spam de pagos
    options.AddPolicy("codes", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15)
        }));
});

// =======================
// Custom Services
// =======================
// Tiempos de vida en el contenedor de DI:
//   - AddScoped:       una instancia POR PETICION HTTP. Es lo normal para todo lo
//                      que usa AppDbContext (que tambien es scoped).
//   - AddSingleton:    una sola instancia para toda la vida de la app.
//   - AddTransient:    una instancia nueva cada vez que alguien la pide.
//   - AddHostedService: un servicio en segundo plano que arranca con la app y
//                      corre en paralelo a las peticiones (tareas programadas).
// Registrar "Interfaz -> Implementacion" permite que los controladores pidan la
// interfaz en su constructor y se pueda cambiar la implementacion (o usar un
// mock en tests) sin tocarlos.
builder.Services.AddScoped<IEmailSender<Usuario>, GmailEmailSender>();
builder.Services.AddScoped<ICodigoVerificacionService, CodigoVerificacionService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, UserContext>();
builder.Services.AddHostedService<SubscriptionBackgroundService>();
builder.Services.AddScoped<IExpoPushSender, ExpoPushSender>();
builder.Services.AddScoped<IPushNotificationService, PushNotificationService>();
builder.Services.AddScoped<INotificationDigestService, NotificationDigestService>();
builder.Services.AddHostedService<NotificationBackgroundService>();
builder.Services.AddScoped<GmailEmailSender>();
builder.Services.AddScoped<PlanValidationService>();
// Timeout explicito: el de HttpClient son 100 s. Brevo y Expo Push son servicios
// de terceros, y esperar minuto y medio a que respondan bloquea la peticion del
// usuario sin ninguna ganancia.
builder.Services.AddHttpClient(Options.DefaultName, cliente =>
{
    cliente.Timeout = TimeSpan.FromSeconds(15);
});

// Anuncios: IOptionsMonitor + reloadOnChange permite apagar los ads editando
// appsettings en el droplet, sin reiniciar el servicio ni publicar en Play.
builder.Services.Configure<AnunciosOptions>(
    builder.Configuration.GetSection(AnunciosOptions.SectionName));

// Inicio de sesión con Google
builder.Services.Configure<GoogleAuthOptions>(
    builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.AddScoped<GoogleTokenValidator>();
 
// =======================
// Health Checks
// =======================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("La cadena de conexión 'DefaultConnection' no está configurada.");
}
builder.Services.AddHealthChecks()
        .AddNpgSql(
        connectionString: connectionString,
        name: "db_check",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["database"]
    )
    .AddCheck("manual", () =>
    {
        var forceUnhealthy = builder.Configuration.GetValue<bool>("ForceUnhealthy");
        return forceUnhealthy
            ? HealthCheckResult.Unhealthy("Forzado para pruebas")
            : HealthCheckResult.Healthy();
    });
// =======================
// Controllers
// =======================
builder.Services.AddSignalR();
builder.Services.AddScoped<AgendaNotificationFilter>();
builder.Services.AddControllers(options => options.Filters.Add<AgendaNotificationFilter>());

var app = builder.Build();

// =======================
// Role seeding
// =======================
await SeedRolesAsync(app.Services);

// =======================
// Pipeline
// =======================
// Cada app.UseX() agrega un middleware. La peticion entra por el primero, baja
// hasta el controlador y la respuesta sube de vuelta en orden inverso. Por eso el
// ORDEN IMPORTA: p. ej. UseAuthentication (quien eres) tiene que ir antes de
// UseAuthorization (que puedes hacer), y SubscriptionCheckMiddleware va despues
// de ambos porque necesita saber que usuario/empresa hace la peticion.
//
// El PRIMERO del pipeline: solo asi captura tambien lo que revienten los demas
// middleware (suscripcion, autenticacion) y no solo los controladores.
app.UseMiddleware<ManejadorGlobalDeExcepciones>();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}
else
{
    app.UseHttpsRedirection();
    app.UseCors("AllowedApps");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SubscriptionCheckMiddleware>();

app.MapControllers();
app.MapHub<AgendaHub>(AgendaHub.Ruta, options => options.CloseOnAuthenticationExpiration = true);
app.MapHealthChecks("/health");

app.Run();

// =======================
// Role seeding method
// =======================
/// <summary>
/// Crea en la base de datos los roles de Identity que la app necesita
/// (SuperAdmin, Admin, Barbero) si todavia no existen. Es idempotente: se puede
/// correr en cada arranque sin duplicar nada.
///
/// Se crea un "scope" manual porque RoleManager es scoped (depende del
/// DbContext) y aqui no estamos dentro de una peticion HTTP que lo cree por
/// nosotros. Reintenta 5 veces con 5 s de espera por si Postgres todavia no
/// acepta conexiones cuando arranca la API (tipico tras reiniciar el servidor).
/// </summary>
static async Task SeedRolesAsync(IServiceProvider services)
{
    string[] roles = ["SuperAdmin", "Admin", "Barbero"];
    for (int attempt = 0; attempt < 5; attempt++)
    {
        try
        {
            using var scope = services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }
            return;
        }
        catch
        {
            await Task.Delay(5000);
        }
    }
}
