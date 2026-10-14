using BarberShop.Models;
using BarberShop.Utils;

namespace BarberShop.Services
{
    /// <summary>
    /// Plantillas de correo del ciclo de suscripcion (aviso de vencimiento,
    /// vencida, urgente, suspendida, bajada a Gratis, renovada). No se registra en
    /// DI: SubscriptionBackgroundService la crea con "new" pasandole el emisor.
    /// </summary>
    public class SubscriptionEmailService
    {
        private readonly GmailEmailSender _emailSender;

        /// <summary>Recibe el emisor de correos que hara el envio real (Brevo).</summary>
        public SubscriptionEmailService(GmailEmailSender emailSender)
        {
            _emailSender = emailSender;
        }

        /// <summary>
        /// Send warning before subscription expires (7, 3, 1 days before)
        /// </summary>
        public async Task SendExpirationWarningEmailAsync(Usuario user, string empresaNombre, int diasRestantes)
        {
            var subject = $"Tu suscripción vence en {diasRestantes} {(diasRestantes == 1 ? "día" : "días")} - Cuadre APP";

            var urgencyColor = diasRestantes <= 1 ? "#dc2626" : diasRestantes <= 3 ? "#f59e0b" : "#3b82f6";

            var body = GetEmailTemplate(
                "⏰ Tu suscripción está por vencer",
                user.NombreCompleto ?? "Usuario",
                $@"
                <p>Tu suscripción de <strong>{empresaNombre}</strong> vencerá pronto.</p>
                
                <div style='text-align: center; margin: 24px 0;'>
                    <div style='background-color: {urgencyColor}; color: white; padding: 16px 32px; border-radius: 12px; display: inline-block;'>
                        <span style='font-size: 14px;'>Tiempo restante</span>
                        <div style='font-size: 36px; font-weight: bold;'>{diasRestantes} {(diasRestantes == 1 ? "día" : "días")}</div>
                    </div>
                </div>
                
                <p>Para evitar interrupciones en tu servicio, te recomendamos renovar antes del vencimiento.</p>
                
                <div style='text-align: center; margin: 24px 0;'>
                    <a href='#' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Renovar ahora
                    </a>
                </div>
                
                <p style='color: #6B7280; font-size: 14px;'>Si ya renovaste, puedes ignorar este mensaje.</p>
                ");

            await _emailSender.SendGenericEmailAsync(user.Email!, subject, body);
        }

        /// <summary>
        /// Send email when subscription has expired (now in grace period)
        /// </summary>
        public async Task SendExpiredEmailAsync(Usuario user, string empresaNombre, int diasDeGracia)
        {
            var subject = "⚠️ Tu suscripción ha vencido - Cuadre APP";

            var body = GetEmailTemplate(
                "⚠️ Suscripción Vencida",
                user.NombreCompleto ?? "Usuario",
                $@"
                <p>La suscripción de <strong>{empresaNombre}</strong> ha vencido.</p>
                
                <div style='background-color: #fef3c7; border-left: 4px solid #f59e0b; padding: 16px; margin: 20px 0; border-radius: 0 8px 8px 0;'>
                    <p style='margin: 0; color: #92400e; font-weight: bold;'>Período de gracia activo</p>
                    <p style='margin: 8px 0 0 0; color: #92400e;'>
                        Tienes <strong>{diasDeGracia} días</strong> para renovar y conservar todas tus funciones.
                    </p>
                </div>

                <p>Durante este período:</p>
                <ul style='color: #4B5563;'>
                    <li>✅ Puedes seguir usando la aplicación</li>
                    <li>✅ Tus datos están seguros</li>
                    <li>⚠️ Verás recordatorios de renovación</li>
                </ul>

                <div style='text-align: center; margin: 24px 0;'>
                    <a href='https://cuadreapp.duckdns.org/#planes' style='background-color: #f59e0b; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Renovar ahora
                    </a>
                </div>

                <p style='color: #6B7280; font-size: 14px;'>
                    Si no renuevas, tu cuenta pasará al plan Gratis: seguirás entrando y con
                    todos tus datos, pero con anuncios y menos funciones.
                </p>
                ");

            await _emailSender.SendGenericEmailAsync(user.Email!, subject, body);
        }

        /// <summary>
        /// Send urgent reminder during grace period (3 days, 1 day before suspension)
        /// </summary>
        public async Task SendUrgentReminderEmailAsync(Usuario user, string empresaNombre, int diasRestantes)
        {
            // Tono rebajado a propósito: lo que viene es una degradación de plan, no
            // un corte de servicio. Un "AVISO URGENTE" rojo por algo que no bloquea
            // nada quema credibilidad para cuando de verdad haga falta alarmar.
            var subject = diasRestantes == 1
                ? "Último día de tu plan - Cuadre APP"
                : $"Quedan {diasRestantes} días de tu plan - Cuadre APP";

            var body = GetEmailTemplate(
                diasRestantes == 1 ? "Último día de tu plan" : $"Quedan {diasRestantes} días de tu plan",
                user.NombreCompleto ?? "Usuario",
                $@"
                <div style='background-color: #fffbeb; border: 2px solid #f59e0b; padding: 20px; border-radius: 12px; margin-bottom: 20px;'>
                    <p style='margin: 0; color: #92400e; text-align: center;'>
                        La cuenta de <strong>{empresaNombre}</strong> pasa al plan Gratis en
                    </p>
                    <p style='margin: 8px 0 0 0; color: #b45309; font-size: 32px; font-weight: bold; text-align: center;'>
                        {diasRestantes} {(diasRestantes == 1 ? "DÍA" : "DÍAS")}
                    </p>
                </div>

                <p>Al pasar al plan Gratis:</p>
                <ul style='color: #4B5563;'>
                    <li>La app móvil empieza a mostrar anuncios</li>
                    <li>Puedes tener hasta 3 servicios activos</li>
                    <li>Ves 1 mes de historial</li>
                    <li>La versión web queda solo para planes pagos</li>
                </ul>

                <p style='color: #065F46; background-color: #ECFDF5; padding: 12px; border-radius: 8px;'>
                    ✅ <strong>Sigues entrando igual.</strong> No se bloquea tu cuenta ni pierdes datos:
                    clientes, citas e historial se mantienen tal cual.
                </p>

                <div style='text-align: center; margin: 24px 0;'>
                    <a href='https://cuadreapp.duckdns.org/#planes' style='background-color: #f59e0b; color: white; padding: 16px 32px; text-decoration: none; border-radius: 8px; font-weight: bold; font-size: 16px;'>
                        RENOVAR AHORA
                    </a>
                </div>
                ");

            await _emailSender.SendGenericEmailAsync(user.Email!, subject, body);
        }

        /// <summary>
        /// Send email when account is suspended
        /// </summary>
        public async Task SendSuspendedEmailAsync(Usuario user, string empresaNombre)
        {
            var subject = "🚫 Tu cuenta ha sido suspendida - Cuadre APP";

            var body = GetEmailTemplate(
                "🚫 Cuenta Suspendida",
                user.NombreCompleto ?? "Usuario",
                $@"
                <div style='background-color: #fef2f2; border: 2px solid #dc2626; padding: 20px; border-radius: 12px; margin-bottom: 20px; text-align: center;'>
                    <p style='margin: 0; font-size: 48px;'>🚫</p>
                    <p style='margin: 12px 0 0 0; color: #dc2626; font-size: 20px; font-weight: bold;'>
                        Cuenta Suspendida
                    </p>
                </div>
                
                <p>La cuenta de <strong>{empresaNombre}</strong> ha sido suspendida debido a falta de pago.</p>
                
                <p><strong>¿Qué significa esto?</strong></p>
                <ul style='color: #4B5563;'>
                    <li>No puedes acceder a la aplicación</li>
                    <li>Tus empleados tampoco pueden acceder</li>
                    <li>No puedes registrar citas ni cobros</li>
                </ul>
                
                <div style='background-color: #ECFDF5; border-left: 4px solid #10B981; padding: 16px; margin: 20px 0; border-radius: 0 8px 8px 0;'>
                    <p style='margin: 0; color: #065F46; font-weight: bold;'>✅ Tus datos están seguros</p>
                    <p style='margin: 8px 0 0 0; color: #065F46;'>
                        Toda tu información (clientes, citas, empleados, historial) se mantiene guardada.
                        Al renovar, recuperarás acceso completo.
                    </p>
                </div>
                
                <p><strong>Para reactivar tu cuenta:</strong></p>
                
                <div style='text-align: center; margin: 24px 0;'>
                    <a href='#' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Renovar suscripción
                    </a>
                </div>
                
                <p style='color: #6B7280; font-size: 14px;'>
                    ¿Necesitas ayuda? Contacta a soporte en CuadreAPP@gmail.com
                </p>
                ");

            await _emailSender.SendGenericEmailAsync(user.Email!, subject, body);
        }

        /// <summary>
        /// Send email when a paid plan expires and the account falls back to the free tier.
        /// Replaces the old "suspended" flow: nobody gets locked out for not paying.
        /// </summary>
        public async Task SendDowngradedToFreeEmailAsync(Usuario user, string empresaNombre)
        {
            var subject = "Tu cuenta pasó al plan Gratis - Cuadre APP";

            var body = GetEmailTemplate(
                "Ahora estás en el plan Gratis",
                user.NombreCompleto ?? "Usuario",
                $@"
                <div style='background-color: #ECFDF5; border: 2px solid #10B981; padding: 20px; border-radius: 12px; margin-bottom: 20px; text-align: center;'>
                    <p style='margin: 0; font-size: 48px;'>✅</p>
                    <p style='margin: 12px 0 0 0; color: #065F46; font-size: 20px; font-weight: bold;'>
                        Sigues teniendo acceso
                    </p>
                </div>

                <p>La suscripción de <strong>{empresaNombre}</strong> venció, así que tu cuenta pasó
                automáticamente al <strong>plan Gratis</strong>. No perdiste nada de tu información.</p>

                <p><strong>Qué cambia:</strong></p>
                <ul style='color: #4B5563;'>
                    <li>La app móvil ahora muestra anuncios</li>
                    <li>Puedes tener hasta 3 servicios activos</li>
                    <li>Ves 1 mes de historial</li>
                    <li>La versión web queda disponible solo para planes pagos</li>
                </ul>

                <div style='background-color: #ECFDF5; border-left: 4px solid #10B981; padding: 16px; margin: 20px 0; border-radius: 0 8px 8px 0;'>
                    <p style='margin: 0; color: #065F46; font-weight: bold;'>Tus datos siguen intactos</p>
                    <p style='margin: 8px 0 0 0; color: #065F46;'>
                        Clientes, citas, empleados e historial se mantienen guardados. Si vuelves a un plan
                        pago, recuperas todo tal como estaba.
                    </p>
                </div>

                <p><strong>¿Quieres quitar los anuncios y recuperar todas las funciones?</strong></p>

                <div style='text-align: center; margin: 24px 0;'>
                    <a href='https://cuadreapp.duckdns.org/#planes' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Ver planes
                    </a>
                </div>

                <p style='color: #6B7280; font-size: 14px;'>
                    ¿Necesitas ayuda? Contacta a soporte en CuadreAPP@gmail.com
                </p>
                ");

            await _emailSender.SendGenericEmailAsync(user.Email!, subject, body);
        }

        /// <summary>
        /// Send email when subscription is successfully renewed
        /// </summary>
        public async Task SendRenewalConfirmationEmailAsync(Usuario user, string empresaNombre, DateTime nuevaFechaVencimiento)
        {
            var subject = "✅ Suscripción renovada exitosamente - Cuadre APP";

            var body = GetEmailTemplate(
                "✅ ¡Suscripción Renovada!",
                user.NombreCompleto ?? "Usuario",
                $@"
                <div style='background-color: #ECFDF5; border: 2px solid #10B981; padding: 20px; border-radius: 12px; margin-bottom: 20px; text-align: center;'>
                    <p style='margin: 0; font-size: 48px;'>🎉</p>
                    <p style='margin: 12px 0 0 0; color: #065F46; font-size: 20px; font-weight: bold;'>
                        ¡Gracias por renovar!
                    </p>
                </div>
                
                <p>Tu suscripción de <strong>{empresaNombre}</strong> ha sido renovada exitosamente.</p>
                
                <div style='background-color: #F3F4F6; padding: 16px; border-radius: 12px; margin: 20px 0;'>
                    <p style='margin: 0; color: #4B5563;'>
                        <strong>Nueva fecha de vencimiento:</strong>
                    </p>
                    <p style='margin: 8px 0 0 0; color: #1F2937; font-size: 20px; font-weight: bold;'>
                        {nuevaFechaVencimiento:dd MMMM yyyy}
                    </p>
                </div>
                
                <p>Ya puedes seguir disfrutando de todas las funcionalidades de Cuadre APP.</p>
                
                <div style='text-align: center; margin: 24px 0;'>
                    <a href='#' style='background-color: #4F46E5; color: white; padding: 14px 28px; text-decoration: none; border-radius: 8px; font-weight: bold;'>
                        Ir a la aplicación
                    </a>
                </div>
                ");

            await _emailSender.SendGenericEmailAsync(user.Email!, subject, body);
        }

        /// <summary>Envuelve el contenido en la plantilla HTML comun de los correos.</summary>
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
        <!-- Header -->
        <div style='text-align: center; margin-bottom: 32px;'>
            <h1 style='color: #4F46E5; font-size: 28px; margin: 0;'>✂️ Cuadre APP</h1>
        </div>
        
        <!-- Card -->
        <div style='background-color: white; border-radius: 16px; padding: 32px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.05);'>
            <h2 style='color: #1F2937; font-size: 24px; margin: 0 0 8px 0;'>{title}</h2>
            <p style='color: #6B7280; margin: 0 0 24px 0;'>Hola, <strong>{userName}</strong></p>
            
            {content}
        </div>
        
        <!-- Footer -->
        <div style='text-align: center; margin-top: 32px;'>
            <p style='color: #9CA3AF; font-size: 12px; margin: 0;'>
                © {DateTime.Now.Year} Cuadre APP. Todos los derechos reservados.
            </p>
        </div>
    </div>
</body>
</html>";
        }
    }
}