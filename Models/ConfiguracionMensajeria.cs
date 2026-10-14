using System.ComponentModel.DataAnnotations;

namespace BarberShop.Models
{
    /// <summary>
    /// Configuración de mensajería por WhatsApp de cada negocio (1:1 con Empresa).
    /// Hereda de BaseEntity: queda automáticamente aislada por tenant.
    /// </summary>
    public class ConfiguracionMensajeria : BaseEntity
    {
        /// <summary>Código de país sin '+', para armar el número internacional. Venezuela = 58.</summary>
        [StringLength(5)]
        public string CodigoPais { get; set; } = "58";

        /// <summary>App destino al enviar: "business" | "personal" | "ask".</summary>
        [StringLength(20)]
        public string AppDestino { get; set; } = "business";

        // Plantillas con variables: {cliente} {negocio} {servicio} {saldo} {fecha}
        [StringLength(1000)]
        public string PlantillaRecordatorio { get; set; } =
            "Hola {cliente} 👋, ha pasado un tiempo desde tu último {servicio}. " +
            "¿Te gustaría agendar tu próxima cita en {negocio}? 💈";

        [StringLength(1000)]
        public string PlantillaDeudor { get; set; } =
            "Hola {cliente}, te recordamos de {negocio} que tienes un saldo pendiente de {saldo}. " +
            "¿Cuándo podrías pasar a liquidarlo? 🙏";

        [StringLength(1000)]
        public string PlantillaConfirmacion { get; set; } =
            "¡Hola {cliente}! Te confirmamos tu cita en {negocio} para el {fecha}. " +
            "¡Te esperamos! ✨";

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    }
}
