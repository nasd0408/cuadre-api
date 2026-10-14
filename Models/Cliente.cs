using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class Cliente : BaseEntity
    {
        [Required]
        [StringLength(150)]
        public string Nombre { get; set; } = null!;

        [StringLength(20)]
        public string? Telefono { get; set; }

        public DateTime? FechaUltimoCorte { get; set; }

        [JsonIgnore]
        public ICollection<Cita> Citas { get; set; } = new List<Cita>();

        public bool Activo { get; set; } = true;
    }
}
