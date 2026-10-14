using BarberShop.Enum;

namespace BarberShop.Models
{
    // Models/Recordatorio.cs
    public class Recordatorio:BaseEntity
    {
        public int ClienteId { get; set; }
        public Cliente Cliente { get; set; } = null!;
        public int ServicioId { get; set; }
        public Servicio Servicio { get; set; } = null!;

        public EstadoRecordatorio Estado { get; set; } = EstadoRecordatorio.Pendiente;
        public DateTime? FechaGestion { get; set; } // Cuando cambió de estado
        public string? Nota { get; set; } // Opcional, por si quiere anotar algo
    }
}
