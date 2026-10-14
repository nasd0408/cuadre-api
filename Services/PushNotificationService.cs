using BarberShop.Data;
using BarberShop.Enum;
using BarberShop.Models;
using Microsoft.EntityFrameworkCore;

namespace BarberShop.Services
{
    /// <summary>
    /// Capa de "negocio" de las notificaciones: decide A QUIEN enviar (que
    /// dispositivos, respetando preferencias) y delega el envio tecnico en
    /// IExpoPushSender. Separar ambas cosas permite cambiar Expo por Firebase
    /// sin tocar esta logica.
    /// </summary>
    public interface IPushNotificationService
    {
        /// <summary>Preferencias de la empresa; si no existen, defaults (todo activado).</summary>
        Task<PreferenciaNotificacion> GetPreferenciasAsync(int empresaId, CancellationToken ct = default);

        /// <summary>
        /// Envía un push a la empresa. Por defecto solo alcanza a administradores
        /// para no exponer información financiera a cuentas de empleados.
        /// </summary>
        Task<int> SendToEmpresaAsync(
            int empresaId,
            string titulo,
            string mensaje,
            string screen,
            bool soloAdministradores = true,
            CancellationToken ct = default);

        /// <summary>
        /// Anuncio a todas las empresas con Anuncios activado (opcionalmente solo no suspendidas).
        /// Devuelve la cantidad de dispositivos alcanzados.
        /// </summary>
        Task<int> SendBroadcastAsync(string titulo, string mensaje, bool soloActivas, CancellationToken ct = default);
    }

    /// <summary>Implementacion sobre la tabla DispositivosPush + Expo.</summary>
    public class PushNotificationService : IPushNotificationService
    {
        private readonly AppDbContext _db;
        private readonly IExpoPushSender _push;
        private readonly ILogger<PushNotificationService> _logger;

        /// <summary>Dependencias inyectadas: BD, emisor de push y logger.</summary>
        public PushNotificationService(
            AppDbContext db,
            IExpoPushSender push,
            ILogger<PushNotificationService> logger)
        {
            _db = db;
            _push = push;
            _logger = logger;
        }

        /// <summary>
        /// Lee las preferencias de la empresa. AsNoTracking le dice a EF que no
        /// vigile los cambios de esta entidad: mas rapido cuando solo se va a leer.
        /// </summary>
        public async Task<PreferenciaNotificacion> GetPreferenciasAsync(int empresaId, CancellationToken ct = default)
        {
            // IgnoreQueryFilters: este servicio corre también en background (sin tenant)
            var prefs = await _db.PreferenciasNotificacion
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.EmpresaId == empresaId, ct);

            return prefs ?? new PreferenciaNotificacion { EmpresaId = empresaId };
        }

        /// <summary>
        /// Junta los tokens de los dispositivos de la empresa (solo admins: los
        /// usuarios sin EmpleadoId), envia el push con "screen" en los datos para
        /// que la app abra esa pantalla al tocarlo, y limpia los tokens muertos.
        /// Devuelve cuantos push acepto Expo.
        /// </summary>
        public async Task<int> SendToEmpresaAsync(
            int empresaId,
            string titulo,
            string mensaje,
            string screen,
            bool soloAdministradores = true,
            CancellationToken ct = default)
        {
            var dispositivos = _db.DispositivosPush
                .Where(d => d.EmpresaId == empresaId);

            if (soloAdministradores)
                dispositivos = dispositivos.Where(d => d.Usuario != null && d.Usuario.EmpleadoId == null);

            var tokens = await dispositivos
                .Select(d => d.Token)
                .Distinct()
                .ToListAsync(ct);

            if (tokens.Count == 0) return 0;

            var result = await _push.SendAsync(
                tokens.Select(t => new ExpoPushMessage(t, titulo, mensaje, new { screen, empresaId })),
                ct);

            await RemoveInvalidTokensAsync(result.InvalidTokens, ct);
            return result.Accepted;
        }

        /// <summary>
        /// Envio masivo (lo usa el SuperAdmin): todos los dispositivos, menos los
        /// de empresas que desactivaron "Anuncios" y, opcionalmente, las
        /// suspendidas. El IQueryable se va componiendo con Where condicionales y
        /// solo se ejecuta una consulta al final.
        /// </summary>
        public async Task<int> SendBroadcastAsync(
            string titulo, string mensaje, bool soloActivas, CancellationToken ct = default)
        {
            // Empresas que desactivaron anuncios
            var optOut = await _db.PreferenciasNotificacion
                .IgnoreQueryFilters()
                .Where(p => !p.Anuncios)
                .Select(p => p.EmpresaId)
                .ToListAsync(ct);

            var dispositivos = _db.DispositivosPush.AsQueryable();

            if (optOut.Count > 0)
                dispositivos = dispositivos.Where(d => !optOut.Contains(d.EmpresaId));

            if (soloActivas)
            {
                var suspendidas = await _db.Empresas
                    .IgnoreQueryFilters()
                    .Where(e => e.Estado == EstadoSuscripcion.Suspendido)
                    .Select(e => e.Id)
                    .ToListAsync(ct);

                if (suspendidas.Count > 0)
                    dispositivos = dispositivos.Where(d => !suspendidas.Contains(d.EmpresaId));
            }

            var tokens = await dispositivos
                .Select(d => d.Token)
                .Distinct()
                .ToListAsync(ct);
            if (tokens.Count == 0) return 0;

            var result = await _push.SendAsync(
                tokens.Select(t => new ExpoPushMessage(t, titulo, mensaje, new { screen = "Dashboard" })),
                ct);

            await RemoveInvalidTokensAsync(result.InvalidTokens, ct);

            _logger.LogInformation(
                "Broadcast aceptado para {Count} dispositivos: {Titulo}",
                result.Accepted,
                titulo);
            return result.Accepted;
        }

        /// <summary>Borra de la BD los dispositivos cuyo token Expo reporto como no registrado.</summary>
        private async Task RemoveInvalidTokensAsync(
            IReadOnlyCollection<string> invalidTokens,
            CancellationToken ct)
        {
            if (invalidTokens.Count == 0) return;

            var devices = await _db.DispositivosPush
                .Where(d => invalidTokens.Contains(d.Token))
                .ToListAsync(ct);

            if (devices.Count == 0) return;

            _db.DispositivosPush.RemoveRange(devices);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Se retiraron {Count} token(es) push inválido(s)",
                devices.Count);
        }
    }
}
