using BarberShop.Enum;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BarberShop.Models
{
    public class Cita : BaseEntity
    {
        [Required]
        public int EmpleadoId { get; set; }
        [JsonIgnore]
        public Empleado Empleado { get; set; } = null!;

        [Required]
        public int ClienteId { get; set; }
        [JsonIgnore]
        public Cliente Cliente { get; set; } = null!;

        public DateTime Fecha { get; set; }
        public EstadoCita Estado { get; set; } = EstadoCita.Pendiente;

        public bool PagadoAlEmpleado { get; set; } = false;

        [MaxLength(100)]
        public string? OfflineId { get; set; }

        // Relación con servicios (muchos a muchos)
        public ICollection<CitaServicio> CitaServicios { get; set; } = new List<CitaServicio>();
        // Calculado a partir del estado
        [NotMapped]
        public bool Cerrada => Estado == EstadoCita.Cerrada;
    }
}
