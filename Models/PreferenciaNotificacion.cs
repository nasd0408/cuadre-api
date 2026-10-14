namespace BarberShop.Models
{
    /// <summary>
    /// Preferencias de notificaciones push por empresa (1:1 con Empresa).
    /// Hereda de BaseEntity: aislada por tenant automáticamente.
    /// </summary>
    public class PreferenciaNotificacion : BaseEntity
    {
        /// <summary>Resumen diario de la mañana (deudores, recordatorios, ayer).</summary>
        public bool DigestDiario { get; set; } = true;

        /// <summary>Aviso de nómina por pagar dentro del digest.</summary>
        public bool AvisoNomina { get; set; } = true;

        /// <summary>Avisos de suscripción (por vencer, morosa, suspendida).</summary>
        public bool AvisoSuscripcion { get; set; } = true;

        /// <summary>Anuncios generales enviados por el equipo de Cuadre.</summary>
        public bool Anuncios { get; set; } = true;

        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    }
}
