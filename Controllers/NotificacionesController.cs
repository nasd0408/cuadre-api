using BarberShop.Data;
using BarberShop.Interfaces;
using BarberShop.Models;
using BarberShop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BarberShop.Controllers
{
    /// <summary>
    /// Notificaciones push: registro del token del telefono, preferencias,
    /// pruebas y envio masivo (broadcast) del SuperAdmin.
    /// </summary>
    [ApiController]
    [Route("api/notificaciones")]
    [Authorize]
    public class NotificacionesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IUserContext _userContext;
        private readonly INotificationDigestService _digest;
        private readonly IPushNotificationService _pushService;
        private readonly ILogger<NotificacionesController> _logger;

        /// <summary>Recibe BD, contexto del usuario, servicios de push/digest y logger por DI.</summary>
        public NotificacionesController(
            AppDbContext context,
            IUserContext userContext,
            INotificationDigestService digest,
            IPushNotificationService pushService,
            ILogger<NotificacionesController> logger)
        {
            _context = context;
            _userContext = userContext;
            _digest = digest;
            _pushService = pushService;
            _logger = logger;
        }

        // Registra o actualiza el token push del dispositivo actual.
        /// <summary>
        /// La app, al iniciar sesion, manda su token de Expo Push. Si ya existe se
        /// actualiza su dueno (upsert); si no, se crea. Sin este token el servidor no
        /// tiene a donde enviar notificaciones.
        /// </summary>
        [HttpPost("registrar-token")]
        public async Task<IActionResult> RegistrarToken([FromBody] RegistrarTokenDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = _userContext.UsuarioId;
            var empresaId = _userContext.EmpresaId;
            if (string.IsNullOrEmpty(userId) || !empresaId.HasValue) return Unauthorized();

            var existente = await _context.DispositivosPush
                .FirstOrDefaultAsync(d => d.Token == dto.Token);

            if (existente == null)
            {
                _context.DispositivosPush.Add(new DispositivoPush
                {
                    UserId = userId,
                    EmpresaId = empresaId.Value,
                    Token = dto.Token,
                    Plataforma = dto.Plataforma ?? "android",
                });
            }
            else
            {
                // El mismo dispositivo puede reasignarse a otro usuario/empresa
                // (re-login en el mismo telefono). Es deliberado y hay que
                // mantenerlo: un barbero que cambia de cuenta en su movil.
                //
                // La consulta de arriba va sin filtro de tenant porque
                // DispositivoPush no es BaseEntity, asi que en teoria alguien que
                // conociera el token Expo de otro podria quedarse con sus avisos.
                // Se registra el cambio de empresa para que ese caso deje rastro
                // en vez de ser invisible; bloquearlo romperia el re-login.
                if (existente.EmpresaId != empresaId.Value)
                {
                    _logger.LogInformation(
                        "Dispositivo push reasignado de empresa {Anterior} a {Nueva} por el usuario {UserId}",
                        existente.EmpresaId, empresaId.Value, userId);
                }

                existente.UserId = userId;
                existente.EmpresaId = empresaId.Value;
                existente.Plataforma = dto.Plataforma ?? existente.Plataforma;
                existente.FechaActualizacion = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Token registrado" });
        }

        // Borra el token (al cerrar sesión) para no seguir enviando a ese dispositivo.
        /// <summary>Borra el token del dispositivo al cerrar sesion.</summary>
        [HttpDelete("token")]
        public async Task<IActionResult> EliminarToken([FromBody] RegistrarTokenDto dto)
        {
            var userId = _userContext.UsuarioId;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var token = await _context.DispositivosPush
                .FirstOrDefaultAsync(d => d.Token == dto.Token && d.UserId == userId);
            if (token != null)
            {
                _context.DispositivosPush.Remove(token);
                await _context.SaveChangesAsync();
            }
            return Ok(new { message = "Token eliminado" });
        }

        // Dispara el digest para la empresa del usuario actual, ahora mismo.
        // Útil para probar en el dispositivo sin esperar a las 9am.
        /// <summary>Envia el digest diario ahora mismo a la empresa del Admin (para probar).</summary>
        [HttpPost("test-digest")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TestDigest()
        {
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            var enviado = await _digest.EnviarDigestEmpresaAsync(empresaId.Value);
            return Ok(new
            {
                enviado = enviado != null,
                mensaje = enviado ?? "Sin novedades hoy (no se envió nada). Registra deudores/recordatorios para ver el digest."
            });
        }

        // Envía una prueba determinista aunque el negocio no tenga actividad.
        /// <summary>Envia una notificacion de prueba fija, haya o no actividad en el negocio.</summary>
        [HttpPost("test")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TestNotification()
        {
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            var dispositivos = await _pushService.SendToEmpresaAsync(
                empresaId.Value,
                "Cuadre está conectado",
                "Las notificaciones funcionan en este dispositivo.",
                "Notificaciones");

            return Ok(new
            {
                enviado = dispositivos > 0,
                dispositivos,
                mensaje = dispositivos > 0
                    ? $"Prueba enviada a {dispositivos} dispositivo(s) administrador(es)."
                    : "No hay un dispositivo administrador registrado. Activa las notificaciones desde la app instalada."
            });
        }

        // ── Preferencias por empresa ──────────────────────────────

        /// <summary>Que avisos quiere recibir la empresa (digest, nomina, suscripcion, anuncios).</summary>
        [HttpGet("preferencias")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<PreferenciasDto>> GetPreferencias()
        {
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            var prefs = await _pushService.GetPreferenciasAsync(empresaId.Value);
            return Ok(ToDto(prefs));
        }

        /// <summary>Guarda las preferencias (crea la fila si no existe).</summary>
        [HttpPut("preferencias")]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<PreferenciasDto>> UpdatePreferencias([FromBody] PreferenciasDto dto)
        {
            var empresaId = _userContext.EmpresaId;
            if (!empresaId.HasValue) return Unauthorized();

            // Query con tenant filter: solo ve la fila de su empresa
            var prefs = await _context.PreferenciasNotificacion.FirstOrDefaultAsync();
            if (prefs == null)
            {
                prefs = new PreferenciaNotificacion(); // EmpresaId lo setea SaveChanges
                _context.PreferenciasNotificacion.Add(prefs);
            }

            prefs.DigestDiario = dto.DigestDiario;
            prefs.AvisoNomina = dto.AvisoNomina;
            prefs.AvisoSuscripcion = dto.AvisoSuscripcion;
            prefs.Anuncios = dto.Anuncios;
            prefs.FechaActualizacion = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(ToDto(prefs));
        }

        // ── Broadcast del SuperAdmin (anuncios a todos) ───────────

        /// <summary>Envio masivo a todas las empresas. Solo SuperAdmin.</summary>
        [HttpPost("broadcast")]
        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Broadcast([FromBody] BroadcastDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var alcanzados = await _pushService.SendBroadcastAsync(
                dto.Titulo.Trim(), dto.Mensaje.Trim(), dto.SoloActivas);

            return Ok(new
            {
                dispositivos = alcanzados,
                message = alcanzados > 0
                    ? $"Anuncio enviado a {alcanzados} dispositivo(s)."
                    : "No hay dispositivos registrados que reciban anuncios."
            });
        }

        /// <summary>Convierte la entidad de preferencias a DTO.</summary>
        private static PreferenciasDto ToDto(PreferenciaNotificacion p) => new()
        {
            DigestDiario = p.DigestDiario,
            AvisoNomina = p.AvisoNomina,
            AvisoSuscripcion = p.AvisoSuscripcion,
            Anuncios = p.Anuncios,
        };

        /// <summary>Cuerpo con el token Expo Push del dispositivo (y la plataforma).</summary>
        public class RegistrarTokenDto
        {
            [Required]
            [StringLength(255)]
            [RegularExpression(
                @"^(ExponentPushToken|ExpoPushToken)\[[^\]]+\]$",
                ErrorMessage = "El token push no tiene un formato válido.")]
            public string Token { get; set; } = string.Empty;

            [StringLength(20)]
            public string? Plataforma { get; set; }
        }

        /// <summary>Interruptores de cada tipo de aviso.</summary>
        public class PreferenciasDto
        {
            public bool DigestDiario { get; set; } = true;
            public bool AvisoNomina { get; set; } = true;
            public bool AvisoSuscripcion { get; set; } = true;
            public bool Anuncios { get; set; } = true;
        }

        /// <summary>Cuerpo del envio masivo: titulo, mensaje y alcance.</summary>
        public class BroadcastDto
        {
            [Required]
            [StringLength(80)]
            public string Titulo { get; set; } = string.Empty;

            [Required]
            [StringLength(300)]
            public string Mensaje { get; set; } = string.Empty;

            /// <summary>Si true, excluye empresas suspendidas.</summary>
            public bool SoloActivas { get; set; } = false;
        }
    }
}
