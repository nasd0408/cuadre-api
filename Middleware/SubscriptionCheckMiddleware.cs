using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Middleware
{
    /// <summary>
    /// Middleware que, en cada peticion autenticada, revisa el estado de la
    /// suscripcion de la empresa:
    ///   - Suspendido (o pasada la FechaBloqueo) -> corta la peticion con 403.
    ///   - Moroso (o pasada la FechaVencimiento)  -> deja pasar, pero agrega
    ///     cabeceras X-Subscription-* para que la app muestre un aviso.
    /// Las rutas de login, pago, etc. se excluyen para que un usuario
    /// suspendido pueda seguir entrando y pagar.
    /// </summary>
    public class SubscriptionCheckMiddleware
    {
        private readonly RequestDelegate _next;

        // Routes that should bypass subscription check
        private readonly string[] _excludedPaths = new[]
        {
            "/api/auth/login",
            "/api/auth/register",
            "/api/auth/google",
            "/api/auth/confirm-email",
            "/api/auth/resend-confirmation",
            "/api/auth/forgot-password",
            "/api/auth/reset-password",
            "/api/auth/refresh",
            "/api/auth/logout",
            "/api/subscription/status",  
            "/swagger",
            "/health",
            "/api/PagosSuscripcion/mis-pagos",
            "/api/PagosSuscripcion/registrar",
            "/api/PagosSuscripcion/landing",
            "/api/ajustes/plan",
        };

        /// <summary>Guarda la referencia al siguiente middleware del pipeline.</summary>
        public SubscriptionCheckMiddleware(RequestDelegate next) => _next = next;

        /// <summary>
        /// Se ejecuta en cada peticion. Ojo: AppDbContext e IUserContext se piden
        /// como PARAMETROS de InvokeAsync y no en el constructor, porque son
        /// scoped (uno por peticion) y el middleware vive toda la app. Si se
        /// inyectaran en el constructor se reutilizaria el mismo DbContext
        /// para todas las peticiones (bug clasico: "captive dependency").
        /// </summary>
        public async Task InvokeAsync(HttpContext context, AppDbContext dbContext, IUserContext userContext)
        {
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // El catálogo público no depende de la suscripción del visitante.
            if (context.Request.Path.StartsWithSegments("/api/publico/empresas") ||
                context.Request.Path.StartsWithSegments("/api/publico/reservas"))
            {
                await _next(context);
                return;
            }

            if (_excludedPaths.Any(p => path.StartsWith(p.ToLower())))
            {
                await _next(context);
                return;
            }

            if (userContext.EmpresaId.HasValue)
            {
                try
                {
                    var empresa = await dbContext.Empresas
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == userContext.EmpresaId);

                    if (empresa != null)
                    {
                        var now = DateTime.UtcNow;

                        if (empresa.Estado == EstadoSuscripcion.Suspendido ||
                           (empresa.FechaBloqueo.HasValue && now > empresa.FechaBloqueo))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                error = "Tu suscripción ha sido suspendida",
                                code = "SUBSCRIPTION_SUSPENDED",
                                estado = "Suspendido",
                                message = "Contacta a soporte para reactivar tu cuenta."
                            });
                            return;
                        }

                        if (empresa.Estado == EstadoSuscripcion.Moroso ||
                           (empresa.FechaVencimiento.HasValue && now > empresa.FechaVencimiento))
                        {
                            var diasRestantes = empresa.FechaBloqueo.HasValue
                                ? (empresa.FechaBloqueo.Value - now).Days
                                : 0;

                            context.Response.Headers.Append("X-Subscription-Status", "Moroso");
                            context.Response.Headers.Append("X-Subscription-Days-Left", diasRestantes.ToString());
                            context.Response.Headers.Append("Access-Control-Expose-Headers", "X-Subscription-Status, X-Subscription-Days-Left");
                        }
                    }
                }
                catch (Exception)
                {
                    // DB caída: devolver 503 para que el front muestre la pantalla de db_error
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "Base de datos no disponible",
                        code = "DB_UNAVAILABLE"
                    });
                    return;
                }
            }

            await _next(context);
        }
    }
}
