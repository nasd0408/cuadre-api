using BarberShop.Helpers;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace BarberShop.Services
{
    /// <summary>Datos que interesan del ID token de Google, ya verificado.</summary>
    public record GoogleUsuario(string Sub, string Email, bool EmailVerificado, string? Nombre);

    /// <summary>
    /// Verifica los ID tokens de Google contra las claves públicas de Google.
    ///
    /// La verificación es el punto crítico de seguridad de todo el flujo: sin ella
    /// cualquiera podría mandar un JSON con el email de otro y entrar como esa persona.
    /// Por eso nunca se confía en el email que envía el cliente, solo en el que viene
    /// firmado dentro del token.
    /// </summary>
    public class GoogleTokenValidator
    {
        private readonly IOptionsMonitor<GoogleAuthOptions> _options;
        private readonly ILogger<GoogleTokenValidator> _logger;

        /// <summary>
        /// IOptionsMonitor (en vez de IOptions) relee la configuracion si cambia
        /// appsettings.json, sin reiniciar la app. Se accede con .CurrentValue.
        /// </summary>
        public GoogleTokenValidator(
            IOptionsMonitor<GoogleAuthOptions> options,
            ILogger<GoogleTokenValidator> logger)
        {
            _options = options;
            _logger = logger;
        }

        /// <summary>true si hay Client IDs de Google configurados.</summary>
        public bool EstaConfigurado => _options.CurrentValue.EstaConfigurado;

        /// <summary>
        /// Devuelve los datos del usuario si el token es válido; null si no lo es.
        /// La libreria de Google descarga sus claves publicas y comprueba la firma,
        /// la expiracion, el emisor y la audiencia (que el token sea para NUESTRA app).
        /// </summary>
        public async Task<GoogleUsuario?> ValidarAsync(string idToken)
        {
            var config = _options.CurrentValue;

            if (!config.EstaConfigurado)
            {
                _logger.LogError(
                    "Se recibió un login de Google pero no hay ClientIds configurados en {Section}",
                    GoogleAuthOptions.SectionName);
                return null;
            }

            if (string.IsNullOrWhiteSpace(idToken))
                return null;

            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(
                    idToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        // Sin Audience, un token emitido para CUALQUIER app de Google
                        // sería aceptado aquí. Es el chequeo que evita que un atacante
                        // reutilice el token que obtuvo en otra aplicación.
                        Audience = config.ClientIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                    });

                if (string.IsNullOrWhiteSpace(payload.Subject) ||
                    string.IsNullOrWhiteSpace(payload.Email))
                {
                    return null;
                }

                return new GoogleUsuario(
                    payload.Subject,
                    payload.Email.Trim().ToLowerInvariant(),
                    payload.EmailVerified,
                    payload.Name);
            }
            catch (InvalidJwtException ex)
            {
                // Token expirado, firma inválida o audiencia equivocada: es lo esperado
                // ante un intento inválido, no un fallo del servidor.
                _logger.LogWarning("ID token de Google rechazado: {Motivo}", ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verificando el ID token de Google");
                return null;
            }
        }
    }
}
