namespace BarberShop.Interfaces
{
    /// <summary>
    /// Contrato generico para enviar correos HTML. Una interfaz define QUE se
    /// puede hacer sin decir COMO; la implementacion concreta (Brevo) se elige
    /// al registrar el servicio en Program.cs.
    /// </summary>
    public interface IAppEmailSender
    {
        /// <summary>Envia un correo con cuerpo HTML al destinatario indicado.</summary>
        Task SendAsync(string toEmail, string subject, string htmlBody);
    }
}
