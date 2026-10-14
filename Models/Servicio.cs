using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class Servicio : BaseEntity
    {
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [StringLength(500)]
        public string? Descripcion { get; set; }

        [Precision(18, 4)]
        [Range(0, double.MaxValue)]
        public decimal Precio { get; set; }

        [Range(1, 600)]
        public int DuracionMinutos { get; set; }
        public int? IntervaloDias { get; set; }

        [Required]
        [StringLength(50)]
        public string Categoria { get; set; } = null!;

        [StringLength(7)]
        public string? ColorHex { get; set; }

        public bool EnPromocion { get; set; } = false;

        public bool Activo { get; set; } = true;

        [StringLength(500)]
        public string? ImagenUrl { get; set; }

        // Relación inversa con citas
        [JsonIgnore]
        public ICollection<CitaServicio> CitaServicios { get; set; } = new List<CitaServicio>();
    }
}
