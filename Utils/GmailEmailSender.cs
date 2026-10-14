using BarberShop.Models;
using Microsoft.AspNetCore.Identity;
using System.Text;
using System.Text.Json;

namespace BarberShop.Utils
{
    /// <summary>
    /// Envia los correos transaccionales de la app (confirmacion, codigos,
    /// bienvenida...). OJO con el nombre: aunque se llama "Gmail", hoy envia a
    /// traves de la API HTTP de Brevo (ver SendEmailAsync al final).
    ///
    /// Implementa IEmailSender&lt;Usuario&gt;, la interfaz que ASP.NET Identity usa
    /// para mandar correos, y ademas expone metodos propios (codigo de
    /// confirmacion, bienvenida, generico).
    /// </summary>
    public class GmailEmailSender : IEmailSender<Usuario>
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GmailEmailSender> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        /// <summary>
        /// IConfiguration lee appsettings.json (y variables de entorno);
        /// IHttpClientFactory crea HttpClients reutilizando conexiones (crear un
        /// "new HttpClient()" por peticion agota los sockets del servidor).
        /// </summary>
        public GmailEmailSender(
            IConfiguration configuration,
            ILogger<GmailEmailSender> logger,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>Correo con un BOTON/enlace para confirmar la cuenta.</summary>
        public async Task SendConfirmationLinkAsync(Usuario user, string email, string confirmationLink)
        {
            var body = GetEmailTemplate(
                "Confirma tu cuenta",
                user.NombreCompleto ?? user.UserName ?? "Usuario",
                $@"
                <p>¡Gracias por registrarte en Cuadre APP!</p>
                <p>Por favor confirma tu cuenta haciendo clic en el siguiente enlace:</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <a href='{confirmationLink}' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Confirmar mi cuenta
                    </a>
                </div>
                <p style='color: #6B7280; font-size: 14px;'>Si no creaste esta cuenta, puedes ignorar este correo.</p>
                ");

            await SendEmailAsync(email, "Confirma tu cuenta - Cuadre APP", body);
        }

        /// <summary>
        /// Correo con enlace para restablecer contrasena. Truco heredado: si el
        /// "enlace" en realidad es un HTML completo, lo manda tal cual (se usa como
        /// via para enviar correos arbitrarios a traves de la interfaz de Identity).
        /// </summary>
        public async Task SendPasswordResetLinkAsync(Usuario user, string email, string resetLink)
        {
            if (resetLink.Contains("<html>") || resetLink.Contains("<!DOCTYPE"))
            {
                await SendEmailAsync(email, "Cuadre APP - Notificación", resetLink);
                return;
            }

            var body = GetEmailTemplate(
                "Restablecer contraseña",
                user.NombreCompleto ?? user.UserName ?? "Usuario",
                $@"
                <p>Recibimos una solicitud para restablecer tu contraseña.</p>
                <p>Haz clic en el siguiente enlace para continuar:</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <a href='{resetLink}' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Restablecer contraseña
                    </a>
                </div>
                <p style='color: #6B7280; font-size: 14px;'>Si no solicitaste esto, puedes ignorar este correo. El enlace expirará en 15 minutos.</p>
                ");

            await SendEmailAsync(email, "Restablecer contraseña - Cuadre APP", body);
        }

        /// <summary>Correo con el codigo de 6 digitos para recuperar la contrasena.</summary>
        public async Task SendPasswordResetCodeAsync(Usuario user, string email, string resetCode)
        {
            var body = GetEmailTemplate(
                "Código de verificación",
                user.NombreCompleto ?? user.UserName ?? "Usuario",
                $@"
                <p>Tu código de verificación es:</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <div style='background-color: #F3F4F6; padding: 20px 40px; border-radius: 12px; display: inline-block;'>
                        <span style='font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #1F2937;'>{resetCode}</span>
                    </div>
                </div>
                <p style='color: #6B7280; font-size: 14px;'>Este código expirará en 15 minutos.</p>
                <p style='color: #6B7280; font-size: 14px;'>Si no solicitaste este código, puedes ignorar este correo.</p>
                ");

            await SendEmailAsync(email, "Código de verificación - Cuadre APP", body);
        }

        /// <summary>Correo con el codigo de 6 digitos para confirmar el email al registrarse.</summary>
        public async Task SendEmailConfirmationCodeAsync(Usuario user, string email, string code)
        {
            var body = GetEmailTemplate(
                "¡Bienvenido a Cuadre APP!",
                user.NombreCompleto ?? user.UserName ?? "Usuario",
                $@"
                <p>¡Gracias por registrarte! Para activar tu cuenta, ingresa el siguiente código:</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <div style='background-color: #F3F4F6; padding: 20px 40px; border-radius: 12px; display: inline-block;'>
                        <span style='font-size: 36px; font-weight: bold; letter-spacing: 8px; color: #1F2937;'>{code}</span>
                    </div>
                </div>
                <p style='color: #6B7280; font-size: 14px;'>Este código expirará en 1 hora.</p>
                <p style='color: #6B7280; font-size: 14px;'>Si no creaste esta cuenta, puedes ignorar este correo.</p>
                ");

            await SendEmailAsync(email, "Confirma tu cuenta - Cuadre APP", body);
        }

        /// <summary>Correo de bienvenida una vez que la cuenta queda activada.</summary>
        public async Task SendWelcomeEmailAsync(Usuario user, string email, string empresaNombre)
        {
            var body = GetEmailTemplate(
                "¡Cuenta activada!",
                user.NombreCompleto ?? user.UserName ?? "Usuario",
                $@"
                <p>Tu cuenta ha sido activada exitosamente.</p>
                <div style='background-color: #ECFDF5; border-left: 4px solid #10B981; padding: 16px; margin: 20px 0; border-radius: 0 8px 8px 0;'>
                    <p style='margin: 0; color: #065F46;'><strong>Empresa:</strong> {empresaNombre}</p>
                    <p style='margin: 8px 0 0 0; color: #065F46;'><strong>Email:</strong> {email}</p>
                </div>
                <p>Ya puedes iniciar sesión y comenzar a gestionar tu negocio.</p>
                <div style='text-align: center; margin: 30px 0;'>
                    <a href='#' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Iniciar sesión
                    </a>
                </div>
                <p style='color: #6B7280; font-size: 14px;'>Tu período de prueba gratuito de 30 días ha comenzado.</p>
                ");

            await SendEmailAsync(email, "¡Bienvenido a Cuadre APP!", body);
        }

        /// <summary>Envia un HTML ya armado por quien llama (sin plantilla).</summary>
        public async Task SendGenericEmailAsync(string toEmail, string subject, string htmlBody)
        {
            await SendEmailAsync(toEmail, subject, htmlBody);
        }

        // =====================================================
        // CORE: Brevo API (HTTPS puerto 443 — nunca bloqueado)
        // =====================================================
        /// <summary>
        /// El envio real: un POST JSON a la API REST de Brevo con la api-key en
        /// una cabecera. Se usa HTTPS (443) en vez de SMTP (587) porque muchos
        /// proveedores cloud bloquean los puertos SMTP. Si falla, registra el error
        /// y relanza la excepcion (throw;) para que quien llamo se entere.
        /// </summary>
        private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            var apiKey = _configuration["Email:BrevoApiKey"];
            var fromEmail = _configuration["Email:FromAddress"] ?? "cuadreapp@gmail.com";
            var fromName = _configuration["Email:FromName"] ?? "Cuadre APP";

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Brevo API key not configured. Email not sent to {Email}", toEmail);
                throw new InvalidOperationException("Email service is not configured");
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("api-key", apiKey);
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                // Brevo requiere que el sender esté verificado en su panel
                var payload = new
                {
                    sender = new { name = fromName, email = fromEmail },
                    to = new[] { new { email = toEmail } },
                    subject = subject,
                    htmlContent = htmlBody
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.brevo.com/v3/smtp/email", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Brevo API error {StatusCode}: {Body}", response.StatusCode, responseBody);
                    throw new Exception($"Brevo API returned {response.StatusCode}: {responseBody}");
                }

                _logger.LogInformation("Email sent via Brevo to {Email} - Subject: {Subject}", toEmail, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
                throw;
            }
        }

        /// <summary>
        /// Envuelve el contenido en la plantilla HTML comun (cabecera, tarjeta,
        /// pie). Los estilos van "inline" porque los clientes de correo (Gmail,
        /// Outlook) ignoran casi todo el CSS en bloques &lt;style&gt;.
        /// </summary>
        private string GetEmailTemplate(string title, string userName, string content)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>
<body style='margin: 0; padding: 0; background-color: #F9FAFB; font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;'>
    <div style='max-width: 600px; margin: 0 auto; padding: 40px 20px;'>
        <div style='text-align: center; margin-bottom: 32px;'>
            <h1 style='color: #4F46E5; font-size: 28px; margin: 0;'>✂️ Cuadre APP</h1>
        </div>
        <div style='background-color: white; border-radius: 16px; padding: 32px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.05);'>
            <h2 style='color: #1F2937; font-size: 24px; margin: 0 0 8px 0;'>{title}</h2>
            <p style='color: #6B7280; margin: 0 0 24px 0;'>Hola, <strong>{userName}</strong></p>
            {content}
        </div>
        <div style='text-align: center; margin-top: 32px;'>
            <p style='color: #9CA3AF; font-size: 12px; margin: 0;'>
                © {DateTime.Now.Year} Cuadre APP. Todos los derechos reservados.
            </p>
            <p style='color: #9CA3AF; font-size: 12px; margin: 8px 0 0 0;'>
                Este es un correo automático, por favor no respondas a este mensaje.
            </p>
        </div>
    </div>
</body>
</html>";
        }
    }
}