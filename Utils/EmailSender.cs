using BarberShop.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace BarberShop.Utils;

    /// <summary>
    /// Implementacion "que no hace nada" de IEmailSender (patron Null Object).
    /// Sirve para desarrollo o tests, cuando no se quiere mandar correos reales.
    /// Hoy no esta registrada en Program.cs (se usa GmailEmailSender).
    /// </summary>
    public class NoOpEmailSender : IEmailSender<Usuario>
    {
        /// <summary>No envia nada; devuelve una tarea ya completada.</summary>
        public Task SendEmailAsync(Usuario user, string subject, string htmlMessage)
        {
            // No hace nada, solo evita errores
            return Task.CompletedTask;
        }

    // Los tres metodos siguientes son "implementaciones explicitas de interfaz"
    // (llevan el prefijo IEmailSender<Usuario>.): solo se pueden llamar a traves
    // de la interfaz, no desde una variable de tipo NoOpEmailSender.
    Task IEmailSender<Usuario>.SendConfirmationLinkAsync(Usuario user, string email, string confirmationLink)
    {
        return Task.CompletedTask;
    }

    Task IEmailSender<Usuario>.SendPasswordResetCodeAsync(Usuario user, string email, string resetCode)
    {
        return Task.CompletedTask;
    }

    Task IEmailSender<Usuario>.SendPasswordResetLinkAsync(Usuario user, string email, string resetLink)
    {
        return Task.CompletedTask;
    }
}

