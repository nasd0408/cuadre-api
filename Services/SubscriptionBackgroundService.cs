using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Helpers;
using BarberShop.Models;
using BarberShop.Utils;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Services
{
    /// <summary>
    /// Tarea en segundo plano que cada hora revisa el ciclo de vida de las
    /// suscripciones de pago:
    ///   Activo --(pasa FechaVencimiento)--> Moroso (periodo de gracia)
    ///   Moroso --(pasa FechaBloqueo)------> baja al plan Gratis (sigue Activo)
    /// y envia avisos por correo y push en cada etapa.
    /// </summary>
    public class SubscriptionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SubscriptionBackgroundService> _logger;

        // Una hora limita el retraso visible al vencer el período de gracia sin
        // generar una carga relevante: la consulta recorre menos de cien empresas.
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

        /// <summary>
        /// Igual que NotificationBackgroundService: se inyecta IServiceProvider
        /// para crear un scope (y un DbContext) nuevo en cada vuelta del bucle.
        /// </summary>
        public SubscriptionBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<SubscriptionBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Espera 1 minuto tras el arranque y luego procesa las suscripciones cada
        /// hora hasta que la app se detiene. El try/catch evita que un error
        /// mate el bucle (una excepcion no capturada aqui detendria el servicio).
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Subscription Background Service started");

            // Wait a bit before first run to let the app start up
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubscriptionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing subscriptions");
                }

                // Wait until next check
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        /// <summary>
        /// Una pasada completa: crea el scope, carga las empresas no suspendidas
        /// (con .Include(e => e.Usuarios), que hace un JOIN para traer tambien sus
        /// usuarios en la misma consulta) y procesa cada una por separado.
        /// </summary>
        private async Task ProcessSubscriptionsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var emailSender = scope.ServiceProvider.GetRequiredService<GmailEmailSender>();
            var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
            var subscriptionEmailService = new SubscriptionEmailService(emailSender);

            var now = DateTime.UtcNow;
            // Los cambios de estado se comprueban cada hora, pero los avisos que
            // dependen solo del número de días se envían en una única ventana
            // diaria para no repetirlos hasta 24 veces.
            var enviarRecordatoriosDelDia = now.Hour == 9;
            _logger.LogInformation("Processing subscriptions at {Time}", now);

            // Get all empresas that need attention
            var empresas = await dbContext.Empresas
                .Include(e => e.Usuarios)
                .Where(e => e.Estado != EstadoSuscripcion.Suspendido)
                .ToListAsync(stoppingToken);

            var fallidas = 0;
            foreach (var empresa in empresas)
            {
                try
                {
                    await ProcessEmpresaAsync(
                        empresa,
                        now,
                        dbContext,
                        subscriptionEmailService,
                        pushService,
                        enviarRecordatoriosDelDia,
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    fallidas++;
                    _logger.LogError(
                        ex,
                        "Error processing subscription for Empresa {EmpresaId} ({Nombre})",
                        empresa.Id,
                        empresa.Nombre);
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
            _logger.LogInformation(
                "Subscription processing completed. Processed {Count} empresas; {Failed} failed",
                empresas.Count,
                fallidas);
        }

        /// <summary>
        /// Aplica las reglas a UNA empresa. Calcula los dias que faltan para el
        /// vencimiento y para el bloqueo y decide entre 3 casos (ver bloques
        /// CASE 1/2/3 abajo). Las empresas en plan Gratis se saltan: no vencen.
        /// </summary>
        private async Task ProcessEmpresaAsync(
            Empresa empresa,
            DateTime now,
            AppDbContext dbContext,
            SubscriptionEmailService emailService,
            IPushNotificationService pushService,
            bool enviarRecordatoriosDelDia,
            CancellationToken stoppingToken)
        {
            var adminUser = empresa.Usuarios?.FirstOrDefault();
            if (adminUser == null)
            {
                _logger.LogWarning("Empresa {EmpresaId} has no users", empresa.Id);
                return;
            }

            // El plan Gratis no vence: no hay avisos de vencimiento que mandar.
            var config = await dbContext.EmpresaConfigs
                .FirstOrDefaultAsync(c => c.EmpresaId == empresa.Id, stoppingToken);

            if (config?.Plan == PlanEmpresa.Gratis)
                return;

            // Push complementario al email (los dueños ven antes el teléfono que el correo).
            // Best-effort: nunca debe romper el procesamiento de suscripciones.
            // Funcion local: solo existe dentro de este metodo y puede usar sus
            // variables (empresa, pushService...) sin pasarlas como parametro.
            async Task PushAsync(string titulo, string mensaje)
            {
                try
                {
                    var prefs = await pushService.GetPreferenciasAsync(empresa.Id, stoppingToken);
                    if (!prefs.AvisoSuscripcion) return;
                    await pushService.SendToEmpresaAsync(
                        empresa.Id,
                        titulo,
                        mensaje,
                        "GestionarCuenta",
                        soloAdministradores: true,
                        stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Push de suscripción falló para empresa {EmpresaId}", empresa.Id);
                }
            }

            // Calculate days until expiration and suspension
            var diasHastaVencimiento = empresa.FechaVencimiento.HasValue
                ? (int)Math.Ceiling((empresa.FechaVencimiento.Value - now).TotalDays)
                : (int?)null;


            var diasHastaBloqueo = empresa.FechaBloqueo.HasValue
                ? (int)Math.Ceiling((empresa.FechaBloqueo.Value - now).TotalDays)
                : (int?)null;


            // ============================================
            // CASE 1: Grace period is over → downgrade to the free plan
            // ============================================
            // Ya no se suspende por falta de pago: la cuenta cae al plan Gratis y sigue
            // operativa (con anuncios y límites reducidos). Suspendido queda reservado
            // para la suspensión manual del SuperAdmin.
            if (empresa.FechaBloqueo.HasValue && now > empresa.FechaBloqueo.Value)
            {
                var defaults = PlanDefaults.CrearConfig(empresa.Id, PlanEmpresa.Gratis);

                if (config != null)
                {
                    PlanDefaults.AplicarA(config, PlanEmpresa.Gratis);
                    config.FechaVencimiento = null;
                }
                else
                {
                    dbContext.EmpresaConfigs.Add(defaults);
                }

                empresa.Estado = EstadoSuscripcion.Activo;
                empresa.FechaVencimiento = null;
                empresa.FechaBloqueo = null;

                // El cambio de acceso es la operación crítica. Se persiste antes
                // de notificar para que una caída de Brevo no deje el plan viejo.
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation(
                    "Empresa {EmpresaId} ({Nombre}) downgraded to free plan",
                    empresa.Id, empresa.Nombre);

                await emailService.SendDowngradedToFreeEmailAsync(adminUser, empresa.Nombre);
                await PushAsync(
                    "Tu cuenta pasó al plan Gratis",
                    "Tu suscripción venció. Sigues teniendo acceso, ahora con anuncios. Toca para ver los planes.");
            }
            // ============================================
            // CASE 2: Should be MOROSO (expired but in grace period)
            // ============================================
            else if (empresa.FechaVencimiento.HasValue && now > empresa.FechaVencimiento.Value)
            {
                if (empresa.Estado != EstadoSuscripcion.Moroso)
                {
                    empresa.Estado = EstadoSuscripcion.Moroso;
                    await dbContext.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation(
                        "Empresa {EmpresaId} ({Nombre}) marked as Moroso",
                        empresa.Id, empresa.Nombre);

                    await emailService.SendExpiredEmailAsync(
                        adminUser,
                        empresa.Nombre,
                        diasHastaBloqueo ?? 0);
                    await PushAsync(
                        "Tu suscripción venció",
                        $"Tienes {diasHastaBloqueo ?? 0} día(s) para renovar antes de pasar al plan Gratis. Toca para pagar.");
                }
                // Send reminder if 3 days or 1 day left
                else if (enviarRecordatoriosDelDia &&
                         (diasHastaBloqueo == 3 || diasHastaBloqueo == 1))
                {
                    await emailService.SendUrgentReminderEmailAsync(
                        adminUser,
                        empresa.Nombre,
                        diasHastaBloqueo.Value);
                    await PushAsync(
                        "⚠️ Renovación urgente",
                        $"Tu cuenta pasa al plan Gratis en {diasHastaBloqueo} día(s). Toca para renovar ahora.");
                }
            }
            // ============================================
            // CASE 3: ACTIVO but approaching expiration
            // ============================================
            else if (empresa.Estado == EstadoSuscripcion.Activo && diasHastaVencimiento.HasValue)
            {
                // Send reminder at 7 days, 3 days, and 1 day before expiration
                if (enviarRecordatoriosDelDia &&
                    (diasHastaVencimiento == 7 || diasHastaVencimiento == 3 || diasHastaVencimiento == 1))
                {
                    await emailService.SendExpirationWarningEmailAsync(
                        adminUser,
                        empresa.Nombre,
                        diasHastaVencimiento.Value);
                    await PushAsync(
                        "Tu suscripción vence pronto",
                        $"Vence en {diasHastaVencimiento} día(s). Renueva a tiempo para no interrumpir tu negocio.");

                    _logger.LogInformation(
                        "Sent {Days}-day warning to Empresa {EmpresaId} ({Nombre})",
                        diasHastaVencimiento, empresa.Id, empresa.Nombre);
                }
            }
        }
    }
}
