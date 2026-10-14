namespace BarberShop.Services
{
    /// <summary>
    /// Envía el digest diario de notificaciones a las 13:00 UTC (9:00 am en Venezuela).
    ///
    /// Hereda de BackgroundService: ASP.NET llama a ExecuteAsync al arrancar y la
    /// tarea corre en paralelo a la API hasta que la app se apaga. Es la forma
    /// mas simple de un "cron" dentro del propio proceso. Limitacion: si hubiera
    /// 2 instancias de la API, el digest se enviaria 2 veces (ahi se necesitaria
    /// un lock distribuido, Hangfire/Quartz, o una cola de mensajes).
    /// </summary>
    public class NotificationBackgroundService : BackgroundService
    {
        private const int HoraEnvioUtc = 13; // 9am Venezuela (UTC-4)

        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<NotificationBackgroundService> _logger;

        /// <summary>
        /// Se inyecta IServiceProvider y no el servicio de digest directamente:
        /// un BackgroundService es singleton y el digest es scoped (usa DbContext).
        /// </summary>
        public NotificationBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<NotificationBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// Bucle infinito: espera hasta la proxima 13:00 UTC, crea un scope nuevo
        /// (un DbContext fresco por ejecucion) y envia los digests. El
        /// CancellationToken se activa al apagar la app y corta la espera.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Notification digest service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TiempoHastaProximoEnvio();
                _logger.LogInformation("Próximo digest en {Horas:F1} h", delay.TotalHours);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var digest = scope.ServiceProvider.GetRequiredService<INotificationDigestService>();
                    await digest.EnviarDigestsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error en el envío del digest diario");
                }
            }
        }

        /// <summary>Cuanto falta para la proxima 13:00 UTC (hoy si no ha pasado, si no manana).</summary>
        private static TimeSpan TiempoHastaProximoEnvio()
        {
            var now = DateTime.UtcNow;
            var next = new DateTime(now.Year, now.Month, now.Day, HoraEnvioUtc, 0, 0, DateTimeKind.Utc);
            if (now >= next) next = next.AddDays(1);
            return next - now;
        }
    }
}
