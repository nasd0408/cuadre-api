using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class CitaServicio: BaseEntity
    {
        [Required]
        public int CitaId { get; set; }
        [JsonIgnore]
        public Cita Cita { get; set; } = null!;

        [Required]
        public int ServicioId { get; set; }
        [JsonIgnore]
        public Servicio Servicio { get; set; } = null!;

        [Precision(18, 4)]
        [Range(0, double.MaxValue)]
        public decimal Precio { get; set; } // Precio del servicio en esa cita

        [Range(1, 10)]
        public int Cantidad { get; set; } = 1;

        // Snapshot por unidad. Null solo para registros de versiones anteriores.
        [Range(1, 600)]
        public int? DuracionMinutos { get; set; }

        [StringLength(250)]
        public string? Observaciones { get; set; } // Opcional, por ejemplo: "Corte + Barba"
    }
}
